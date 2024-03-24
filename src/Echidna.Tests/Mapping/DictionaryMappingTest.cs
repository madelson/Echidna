using Medallion.Data.Mapping;
using System.Dynamic;

namespace Medallion.Data.Tests.Mapping;

internal class DictionaryMappingTest
{
    [Test]
    public void TestCanMapToIReadOnlyDictionary([ValueSource(typeof(TestHelper), nameof(TestHelper.DbsToTest))] Db db)
    {
        using var reader = db.Read("SELECT 1 AS A, 'str' AS b, 2.5 AS C, NULL AS d");
        Assert.IsTrue(reader.Value.Read());

        var dict = MappingDelegateProvider.GetMappingDelegate<IReadOnlyDictionary<string, object>>(reader.Value)();

        Assert.AreEqual(1, dict["A"]);
        Assert.AreEqual("str", dict["B"]);
        Assert.AreEqual(2.5, dict["c"]);
        Assert.AreEqual(null, dict["d"]);
        Assert.IsInstanceOf<Dictionary<string, object>>(dict);
    }

    [Test]
    public void TestCanMapToIDictionary([ValueSource(typeof(TestHelper), nameof(TestHelper.DbsToTest))] Db db)
    {
        using var reader = db.Read("SELECT 1 AS A, 'str' AS b, 2.5 AS C, NULL AS d");
        Assert.IsTrue(reader.Value.Read());

        var dict = MappingDelegateProvider.GetMappingDelegate<IDictionary<string, object>>(reader.Value)();

        Assert.AreEqual(1, dict["A"]);
        Assert.AreEqual("str", dict["B"]);
        Assert.AreEqual(2.5, dict["c"]);
        Assert.AreEqual(null, dict["d"]);
        Assert.IsInstanceOf<Dictionary<string, object>>(dict);
    }

    [Test]
    public void TestCanMapToDictionary([ValueSource(typeof(TestHelper), nameof(TestHelper.DbsToTest))] Db db)
    {
        using var reader = db.Read("SELECT 1 AS A, 'str' AS b, 2.5 AS C, NULL AS d");
        Assert.IsTrue(reader.Value.Read());

        var dict = MappingDelegateProvider.GetMappingDelegate<Dictionary<string, object>>(reader.Value)();
        
        Assert.AreEqual(1, dict["A"]);
        Assert.AreEqual("str", dict["B"]);
        Assert.AreEqual(2.5, dict["c"]);
        Assert.AreEqual(null, dict["d"]);
    }

    [Test]
    public void TestThrowsOnDuplicateKeys([ValueSource(typeof(TestHelper), nameof(TestHelper.DbsToTest))] Db db)
    {
        using var reader = db.Read("SELECT 1 AS A, 2 AS a");
        Assert.IsTrue(reader.Value.Read());

        var ex = Assert.Throws<ColumnMappingException>(() => MappingDelegateProvider.GetMappingDelegate<Dictionary<string, int>>(reader.Value)());
        Assert.IsInstanceOf<ArgumentException>(ex!.InnerException);
        Assert.That(ex.InnerException!.Message, Does.StartWith("An item with the same key has already been added"));
    }

    [Test]
    public void TestCanConvertValues([ValueSource(typeof(TestHelper), nameof(TestHelper.DbsToTest))] Db db)
    {
        using var reader = db.Read("SELECT 2.0 AS a, CAST(1 AS FLOAT) AS b, CAST(NULL AS CHAR(10)) AS c");
        Assert.IsTrue(reader.Value.Read());

        var dict = MappingDelegateProvider.GetMappingDelegate<Dictionary<string, int?>>(reader.Value)();

        Assert.AreEqual(2, dict["A"]);
        Assert.AreEqual(1, dict["B"]);
        Assert.AreEqual(null, dict["C"]);
    }

    [Test]
    public void TestAttemptsToConvertAllValues([ValueSource(typeof(TestHelper), nameof(TestHelper.DbsToTest))] Db db)
    {
        using var reader = db.Read("SELECT 1 AS a, 'b' as b");
        Assert.IsTrue(reader.Value.Read());

        var mapper = MappingDelegateProvider.GetMappingDelegate<Dictionary<string, int?>>(reader.Value);
        var ex = Assert.Throws<ColumnMappingException>(() => mapper());
        Assert.That(ex!.Message, Does.Contain("column 1 (System.String b)"));
        Assert.IsInstanceOf<InvalidCastException>(ex.InnerException);
    }

    [Test]
    public void TestCreatesCaseInsensitiveDictionaries([ValueSource(typeof(TestHelper), nameof(TestHelper.DbsToTest))] Db db)
    {
        using var reader = db.Read("SELECT 1 AS apple");
        Assert.IsTrue(reader.Value.Read());

        var dict = MappingDelegateProvider.GetMappingDelegate<Dictionary<string, int>>(reader.Value)();
        Assert.IsTrue(dict.ContainsKey("apple"));
        Assert.IsTrue(dict.ContainsKey("APPLE"));
        Assert.IsTrue(dict.ContainsKey("APple"));
        Assert.IsTrue(dict.ContainsKey("apPLE"));
        Assert.AreEqual(1, dict.Count);
    }

    [Test]
    public void TestMapToSortedDictionary()
    {
        using var reader = Db.MySql.Read("SELECT 1 AS C, 2 AS A, 3 AS b");
        Assert.IsTrue(reader.Value.Read());

        var dict = MappingDelegateProvider.GetMappingDelegate<SortedDictionary<string, int>>(reader.Value)();
        CollectionAssert.AreEqual(
            new KeyValuePair<string, int>[] { new("A", 2), new("b", 3), new("C", 1) },
            dict
        );
    }

    // TODO test map to dictionary deriving from non-nullable reference type (using .NET 7 NullabilityInfoContext)
}
