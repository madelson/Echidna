using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace Medallion.Data;

/// <summary>
/// An LFRU-like cache
/// </summary>
internal sealed class MicroCache<TKey, TValue> where TKey : notnull
{
    // ALGORITHM:
    // For ~LFU, we store use counts with each cache entry which get updated when that
    // entry is retrieved from the cache. When we go to add a new entry to the cache and
    // the cache is full, we examine N random entries and evict the one with the smallest
    // use count. This approach is inspired by the .NET RegexCache.
    //
    // A flaw of this approach is that an entry which is initially used a lot but then stops
    // being relevant may never get evicted. To solve for that we have an ~LRU element.
    // The cache has an "age" counter which upticks with every M writes to the cache. Cache
    // entries also store the age value from when they were last used. When evaluating an
    // entries use count for incrementing or for eviction, we first divide it by 2^k, where
    // k is the difference between the cache's current age and the stored age. This "decay"
    // mechanism ensures that we don't retain items which were once frequently used but no
    // longer are.

    internal const int WritesPerAgeFraction = 2;

    private readonly ConcurrentDictionary<TKey, Entry> _items = new();
    private readonly int _writesPerAge;

    // written only behind Lock
    private uint _currentAge;
    // read/written only behind Lock
    private int _spaceRemaining;
    private int _writesRemainingUntilNextAge;
    private EvictionHelper? _evictionHelper;

    private object Lock => this._items;

    public MicroCache(int maxCount)
    {
        Invariant.Require(maxCount >= 1);

        this._spaceRemaining = maxCount;
        this._writesPerAge = Math.Max(maxCount / WritesPerAgeFraction, 1);
        this._writesRemainingUntilNextAge = this._writesPerAge;
    }

    public bool TryGetValue(TKey key, [MaybeNullWhen(returnValue: false)] out TValue value)
    {
        if (this._items.TryGetValue(key, out var entry))
        {
            Touch(ref entry.UsageStamp, Volatile.Read(ref this._currentAge));
            value = entry.Value;
            return true;
        }

        value = default;
        return false;
    }

    public TValue GetOrAdd(TKey key, Func<TKey, TValue> valueFactory)
    {
        Invariant.Require(valueFactory != null);

        return this.TryGetValue(key, out var existing) ? existing : this.AddOrGetValue(key, valueFactory(key));
    }

    public TValue GetOrAdd<TArg>(TKey key, Func<TKey, TArg, TValue> valueFactory, TArg factoryArgument)
    {
        Invariant.Require(valueFactory != null);

        return this.TryGetValue(key, out var existing) ? existing : this.AddOrGetValue(key, valueFactory(key, factoryArgument));
    }

    private TValue AddOrGetValue(TKey key, TValue value)
    {
        var items = this._items;
        var entry = new Entry(value);
        lock (this.Lock)
        {
            var spaceRemaining = this._spaceRemaining;
            if (spaceRemaining != 0)
            {
                Invariant.Require(spaceRemaining > 0);
                if (items.TryAdd(key, entry))
                {
                    SetEntryUsageStampAndUpdateCurrentAgeIfNeeded();
                    this._spaceRemaining = spaceRemaining - 1;
                    return value;
                }
            }
            else
            {
                // initialize before TryAdd so we have exactly MAX items
                var evictionHelper = this._evictionHelper ??= new(this._items);
                if (items.TryAdd(key, entry))
                {
                    var currentAge = SetEntryUsageStampAndUpdateCurrentAgeIfNeeded();
                    evictionHelper.Evict(items, currentAge, key, entry);
                    return value;
                }
            }

            // look up inside the lock so that the entry can't get evicted first
            return items[key].Value;

            uint SetEntryUsageStampAndUpdateCurrentAgeIfNeeded()
            {
                Invariant.Require(this._writesRemainingUntilNextAge >= 0);
                uint currentAge;
                if (--this._writesRemainingUntilNextAge == 0)
                {
                    currentAge = ++this._currentAge;
                    this._writesRemainingUntilNextAge = this._writesPerAge;
                }
                else { currentAge = this._currentAge; }

                WriteUsageStamp(age: currentAge, useCount: 1, ref entry.UsageStamp);
                return currentAge;
            }
        }
    }

    internal static void Touch(ref ulong usageStamp, uint currentAge)
    {
        var (age, useCount) = ReadUsageStamp(ref usageStamp);
        if (age != currentAge) { AdjustForCurrentAge(ref age, ref useCount, currentAge); }
        if (useCount != uint.MaxValue) { ++useCount; }
        WriteUsageStamp(age, useCount, ref usageStamp);
    }

    internal static uint GetUseCount(ref ulong usageStamp, uint currentAge)
    {
        var (age, useCount) = ReadUsageStamp(ref usageStamp);
        if (age != currentAge)
        {
            AdjustForCurrentAge(ref age, ref useCount, currentAge);
            WriteUsageStamp(age, useCount, ref usageStamp);
        }
        return useCount;
    }

    private static void AdjustForCurrentAge(ref uint age, ref uint useCount, uint currentAge)
    {
        Invariant.Require(age != currentAge);

        var ageDelta = currentAge - age;
        if (ageDelta < 32)
        {
            // divide by 2 for each 1 difference in age
            useCount >>= (int)ageDelta;
        }
        else
        {
            // shifting more than 31 places wraps around, but that large an age difference should just
            // zero out the value
            useCount = 0;
        }
        age = currentAge;
    }

    private static (uint Age, uint UseCount) ReadUsageStamp(ref ulong usageStamp)
    {
        var usageStampValue = Volatile.Read(ref usageStamp);
        return ((uint)(usageStampValue >> 32), (uint)(usageStampValue & uint.MaxValue));
    }

    private static void WriteUsageStamp(uint age, uint useCount, ref ulong usageStamp) =>
        Volatile.Write(ref usageStamp, (((ulong)age) << 32) | useCount);

    private sealed class EvictionHelper
    {
        // Based on RegexCache.cs
        private const int MaxItemsToExamineOnEvict = 30;

        private readonly Random _random = new();
        private readonly (TKey Key, Entry Entry)[] _entries;

        public EvictionHelper(ConcurrentDictionary<TKey, Entry> items)
        {
            var entries = new (TKey, Entry)[items.Count];
            var i = 0;
            foreach (var (key, entry) in items)
            {
                entries[i++] = (key, entry);
            }
            this._entries = entries;
            Invariant.Require(i == entries.Length);
        }

        public void Evict(ConcurrentDictionary<TKey, Entry> items, uint currentAge, TKey newKey, Entry newEntry)
        {
            var random = this._random;
            var entries = this._entries;
            var examineAllItems = entries.Length <= MaxItemsToExamineOnEvict;
            
            var indexToEvict = random.Next(entries.Length);
            var minUseCount = GetUseCount(ref entries[indexToEvict].Entry.UsageStamp, currentAge);

            if (examineAllItems)
            {
                var baseIndex = indexToEvict;
                for (var i = 1; i < entries.Length; ++i)
                {
                    Examine((baseIndex + i) % entries.Length);
                }
            }
            else
            {
                for (var i = 1; i < MaxItemsToExamineOnEvict; ++i)
                {
                    Examine(random.Next(entries.Length));
                }
            }

            var removed = items.TryRemove(entries[indexToEvict].Key, out _);
            Invariant.Require(removed);
            entries[indexToEvict] = (newKey, newEntry);

            void Examine(int indexToExamine)
            {
                var useCount = GetUseCount(ref entries[indexToExamine].Entry.UsageStamp, currentAge);
                if (useCount < minUseCount)
                {
                    minUseCount = useCount;
                    indexToEvict = indexToExamine;
                }
            }
        }
    }

    [DebuggerDisplay("{DebugView}")]
    private sealed class Entry
    {
        public ulong UsageStamp;

        public Entry(TValue value) 
        { 
            this.Value = value;
        }

        public TValue Value { get; }

        private string DebugView
        {
            get
            {
                var usageStamp = ReadUsageStamp(ref this.UsageStamp);
                return $"Value='{this.Value}', Age={usageStamp.Age}, UseCount={usageStamp.UseCount}";
            }
        }
    }
}
