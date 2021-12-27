using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Medallion.Data;

// TODO 2 ideas:
// (1) use a "random sample eviction" approach ala RegexCache (an issue with that specific impl is that it is optimized for 
// accessing the same item over and over again in sequence which isn't really what we expect here)
// (2) use the current pruning approach but always set ts=count
// (3) Use LFU + aging but with random sample eviction:
//  - each node tracks lastTouchedAge & use count
//  - lock on insert if full and evict 1 using to random sample
//  - for eviction, sort by use count >> (age - lastTouchedAge)
//  - bump cache's age every N insertions (N = max count / 2?)
//  - when a node is touched, set lastTouchedAge = age, usecount++

internal sealed class MicroCache<TKey, TValue> where TKey : notnull
{
    private readonly ConcurrentDictionary<TKey, Entry> _items = new();
    private readonly int _pruneThreshold;
    private long _count;

    public MicroCache(int maxCount)
    {
        Invariant.Require(maxCount >= 1);
        this._pruneThreshold = maxCount + 1;
    }

    public bool TryGetValue(TKey key, [MaybeNullWhen(returnValue: false)] out TValue value)
    {
        if (this._items.TryGetValue(key, out var entry))
        {
            entry.MarkUsed();
            value = entry.Value;
            return true;
        }

        value = default;
        return false;
    }

    public bool TryAdd(TKey key, TValue newValue, [MaybeNullWhen(returnValue: true)] out TValue existingValue)
    {
        while (true)
        {
            if (this._items.TryAdd(key, new Entry(newValue)))
            {
                if (Interlocked.Increment(ref this._count) == this._pruneThreshold) { Task.Run(this.Prune); }
                existingValue = default;
                return true;
            }

            if (this.TryGetValue(key, out existingValue))
            {
                return false;
            }
        }
    }

    private void Prune()
    {
        var pruneCount = Math.Max(this._pruneThreshold / 4, 1);
        var leastUsedKeys = new PriorityQueue<TKey, int>(initialCapacity: pruneCount);
        while (true)
        {
            // age all entries and determine the least-used N
            foreach (var (key, entry) in this._items)
            {
                var usageCount = entry.GetAndAgeUsageCount();
                leastUsedKeys.Enqueue(key, -usageCount); // -usageCount so Dequeue() removes the most used
                if (leastUsedKeys.Count > pruneCount) { leastUsedKeys.Dequeue(); }
            }

            // prune the least used N
            Invariant.Require(leastUsedKeys.Count == pruneCount);
            foreach (var (key, _) in leastUsedKeys.UnorderedItems)
            {
                var removed = this._items.TryRemove(key, out _);
                Invariant.Require(removed);
            }

            // After we finish removing, lower the count appropriately. In extreme cases this won't
            // get us below the prune threshold (because while we were pruning loads more entries were added)
            // so just keep pruning in that case. Otherwise, we're done.
            if (Interlocked.Add(ref this._count, -pruneCount) < this._pruneThreshold)
            {
                return;
            }
        }
    }

    private sealed class Entry
    {
        private int _usageCount = 1;

        public Entry(TValue value) { this.Value = value; }

        public TValue Value { get; }

        public void MarkUsed()
        {
            var usageCount = Interlocked.Increment(ref this._usageCount);
            // if we overflow int, attempt to roll back the change (if we don't, someone else will)
            if (usageCount <= 0)
            {
                Interlocked.CompareExchange(ref this._usageCount, value: int.MaxValue, comparand: usageCount);
            }
        }

        public int GetAndAgeUsageCount()
        {
            var usageCount = Volatile.Read(ref this._usageCount);
            
            // if we're in an overflow state, just age as if we were int.MaxValue
            if (usageCount <= 0)
            {
                Volatile.Write(ref this._usageCount, int.MaxValue / 2);
                return int.MaxValue;
            }

            var agedUsages = usageCount >> 2;
            Interlocked.Add(ref this._usageCount, -(usageCount - agedUsages));
            return usageCount;
        }
    }
}
