using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Medallion.Data.Tests.Utilities;

internal class MicroCacheTest
{
    [Test]
    public void TestBasicApi()
    {
        var cache = new MicroCache<string, int?>(maxCount: 5000);

        Assert.IsFalse(cache.TryGetValue("a", out var existing));
        Assert.IsNull(existing);

        Assert.AreEqual(23, cache.GetOrAdd("a", _ => 23));

        Assert.IsTrue(cache.TryGetValue("a", out existing));
        Assert.AreEqual(23, existing);

        Assert.AreEqual(23, cache.GetOrAdd("a", _ => 1));
    }

    [Test]
    public void TestEvictsLeastUsed()
    {
        var cache = new MicroCache<int, string>(maxCount: 8);
        cache.GetOrAdd(0, _ => "a");
        cache.GetOrAdd(1, _ => "b");
        cache.GetOrAdd(2, _ => "c");
        cache.GetOrAdd(3, _ => "d");
        cache.GetOrAdd(4, _ => "e");
        cache.GetOrAdd(5, _ => "f");
        cache.GetOrAdd(6, _ => "g");
        cache.GetOrAdd(7, _ => "h");

        var usages = new Dictionary<int, int>
        {
            [0] = 3,
            [1] = 5,
            [2] = 0,
            [3] = 2,
            [4] = 4,
            [5] = 7,
            [6] = 100,
            [7] = 1,
        };
        Parallel.ForEach(usages, kvp =>
        {
            var (key, count) = kvp;
            Parallel.For(0, count, i => 
            { 
                Assert.IsTrue(cache.TryGetValue(key, out _));
                Thread.Yield();
            });
        });

        cache.GetOrAdd(8, _ => "i");
        
        Assert.IsFalse(cache.TryGetValue(2, out _));
        Assert.IsTrue(cache.TryGetValue(7, out _));
        Assert.IsTrue(cache.TryGetValue(0, out _));
        Assert.IsTrue(cache.TryGetValue(1, out _));
        Assert.IsTrue(cache.TryGetValue(3, out _));
        Assert.IsTrue(cache.TryGetValue(4, out _));
        Assert.IsTrue(cache.TryGetValue(5, out _));
        Assert.IsTrue(cache.TryGetValue(6, out _));
        Assert.IsTrue(cache.TryGetValue(8, out _));
    }

    [Test]
    public void TestEvictsItemsWhichWereOnceFrequenlyUsedButNoLongerAre()
    {
        const int MaxCount = 20;
        const int WritesPerAge = MaxCount / MicroCache<int, string>.WritesPerAgeFraction;
        var cache = new MicroCache<int, string>(MaxCount);
        cache.GetOrAdd(-1, _ => "initial"); // use count 1, age 0
        for (var i = 0; i < 63; ++i)
        {
            if (!cache.TryGetValue(-1, out var cached) || cached != "initial")
            {
                Assert.Fail();
            }
        } // use count 64, age 0

        for (var i = 0; i < 5 * WritesPerAge; ++i)
        {
            cache.GetOrAdd(i, _ => "x");
        } // after 5*writesPerAge writes, age is now 5

        Assert.IsTrue(cache.TryGetValue(-1, out var stillCached)); // use count (64/32=2)+1=3, age 5
        Assert.AreEqual("initial", stillCached);

        for (var i = 0; i < MaxCount; ++i)
        {
            for (var j = 0; j < 4; ++j) { cache.GetOrAdd(1000_000 + i, _ => "z"); }
        }

        Assert.IsFalse(cache.TryGetValue(-1, out var notCached));
        Assert.IsNull(notCached);
    }

    [Test]
    public void TestUsageStampHandling()
    {
        uint currentAge = 0;
        ulong usageStamp = 0;

        Assert.AreEqual(0, GetUseCount());
        Touch();
        Assert.AreEqual(1, GetUseCount());
        Touch();
        Assert.AreEqual(2, GetUseCount());
        usageStamp = uint.MaxValue;
        Assert.AreEqual(uint.MaxValue, GetUseCount());
        Touch();
        Assert.AreEqual(uint.MaxValue, GetUseCount()); // no overflow

        currentAge += 2;
        var usageStampSnapshot = usageStamp;
        Assert.AreEqual(uint.MaxValue / 4, GetUseCount());
        Assert.AreNotEqual(usageStampSnapshot, usageStamp); // updated

        currentAge += 1;
        Touch();
        Assert.AreEqual((uint.MaxValue / 8) + 1, GetUseCount());

        currentAge = uint.MaxValue;
        Assert.AreEqual(0, GetUseCount());
        for (var i = 0; i < 100; ++i) { Touch(); }
        Assert.AreEqual(100, GetUseCount());

        ++currentAge;
        Assert.AreEqual(50, GetUseCount()); // wraps around

        void Touch() => MicroCache<object, object>.Touch(ref usageStamp, currentAge);
        uint GetUseCount() => MicroCache<object, object>.GetUseCount(ref usageStamp, currentAge);
    }
}
