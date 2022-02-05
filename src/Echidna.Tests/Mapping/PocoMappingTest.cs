using Medallion.Data.Mapping;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Medallion.Data.Tests.Mapping;

internal class PocoMappingTest
{
    [Test]
    public void TestCanMapToConstructorRegardlessOfOrder()
    {
        using var reader = Db.MariaDb.Read("SELECT 100 AS B, 'x' AS A");
        Assert.IsTrue(reader.Value.Read());

        var value = MappingDelegateProvider.GetMappingDelegate<HasConstructor>(reader.Value)();
        Assert.AreEqual("x", value.A);
        Assert.AreEqual(100, value.B);
        Assert.AreEqual(true, value.C);
    }

    private class HasConstructor
    {
        public HasConstructor(string a, int b = -1, bool c = true)
        {
            this.A = a;
            this.B = b;
            this.C = c;
        }

        public string A { get; }
        public int B { get; }
        public bool C { get; }
    }

    [Test]
    public void TestCanMapToPropertiesAndFields()
    {
        using var reader = Db.MySql.Read("SELECT 5 AS property, 'five' AS field");
        Assert.IsTrue(reader.Value.Read());

        var value = MappingDelegateProvider.GetMappingDelegate<HasPropertyAndField>(reader.Value)();
        Assert.AreEqual(5, value.Property);
        Assert.AreEqual("five", value.Field);
    }

    private class HasPropertyAndField
    {
        public int Property { get; set; }
#pragma warning disable CS0649
        public string? Field;
#pragma warning restore CS0649
    }

    [Test]
    public void TestPrefersPropertyOverField()
    {
        using var reader = Db.Oracle.Read("SELECT 2.3 AS value");
        Assert.IsTrue(reader.Value.Read());

        var value = MappingDelegateProvider.GetMappingDelegate<HasPropertyAndFieldWithSameName>(reader.Value)();
        Assert.AreEqual(2.3, value.Value);
        Assert.AreEqual(0, value.value);
    }

    private class HasPropertyAndFieldWithSameName
    {
        public double Value { get; set; }
#pragma warning disable CS0649
        public double value;
#pragma warning restore CS0649
    }

    [Test]
    public void TestPrefersExactCaseMatchOverMiscasedMatch()
    {
        using var reader = Db.SqlServer.Read("SELECT 2000 AS DoesItMatch");
        Assert.IsTrue(reader.Value.Read());

        var value = MappingDelegateProvider.GetMappingDelegate<HasExactCaseMatchAndCaseMismatch>(reader.Value)();
        Assert.AreEqual(2000, value.DoesItMatch);
        Assert.AreEqual(0, value.Doesitmatch);
    }

    private class HasExactCaseMatchAndCaseMismatch
    {
        public long Doesitmatch { get; set; }
        public long DoesItMatch { get; set; }    
    }

    [Test]
    public void TestCanMapToConstructorsPropertiesAndFields()
    {
        using var reader = Db.Postgres.Read("SELECT 1 AS a, '2' AS b, 3 AS c");
        Assert.IsTrue(reader.Value.Read());

        var value = MappingDelegateProvider.GetMappingDelegate<HasConstructorPropertyAndField>(reader.Value)();
        Assert.AreEqual(1, value.A);
        Assert.AreEqual("2", value.B);
        Assert.AreEqual(3, value.c);
    }

    private class HasConstructorPropertyAndField
    {
        public HasConstructorPropertyAndField(int a) { this.A = a; }

        public int A { get; }
        public string? B { get; set; }
#pragma warning disable CS0649
        public double? c;
#pragma warning restore CS0649
    }

    [Test]
    public void TestIgnoresByRefConstructor()
    {
        using var reader = Db.SystemDataSqlServer.Read("SELECT 1 AS i, 2 AS j");
        Assert.IsTrue(reader.Value.Read());

        var value = MappingDelegateProvider.GetMappingDelegate<HasByRefConstructor>(reader.Value)();
        Assert.AreEqual(2, value.J);
    }

    private class HasByRefConstructor
    {
        public HasByRefConstructor() { }
        public HasByRefConstructor(ref int i) => throw new NotSupportedException(i.ToString());

        public int J { get; set; }
    }

    [Test]
    public void TestPrefersConstructorWithMoreBoundColumns()
    {
        using var readerA = Db.MariaDb.Read("SELECT 10 AS a");
        Assert.IsTrue(readerA.Value.Read());

        var value = MappingDelegateProvider.GetMappingDelegate<HasMultipleConstructors>(readerA.Value)();
        Assert.AreEqual((10, 0, 0), (value.A, value.B, value.C));
        Assert.AreEqual(1, value.Constructor);

        using var readerB = Db.MariaDb.Read("SELECT 11 AS a, 21 AS b");
        Assert.IsTrue(readerB.Value.Read());

        value = MappingDelegateProvider.GetMappingDelegate<HasMultipleConstructors>(readerB.Value)();
        Assert.AreEqual((11, 21, 0), (value.A, value.B, value.C));
        Assert.AreEqual(2, value.Constructor);

        using var readerC = Db.MariaDb.Read("SELECT 12 AS a, 22 AS b, 32 AS c");
        Assert.IsTrue(readerC.Value.Read());

        value = MappingDelegateProvider.GetMappingDelegate<HasMultipleConstructors>(readerC.Value)();
        Assert.AreEqual((12, 22, 32), (value.A, value.B, value.C));
        Assert.AreEqual(3, value.Constructor);
    }

    private class HasMultipleConstructors
    {
        public HasMultipleConstructors(uint a, uint x = 2, uint y = 3, uint z = 4) { this.A = a; this.Constructor = 1; }
        public HasMultipleConstructors(uint a, uint b) { this.A = a; this.B = b; this.Constructor = 2; }
        public HasMultipleConstructors(uint a, uint b, uint c) { this.A = a; this.B = b; this.C = c; this.Constructor = 3; }

        public uint A { get; }
        public uint B { get; }
        public uint C { get; }
        public int Constructor { get; }
    }

    [Test]
    public void TestDetectsNullableReferenceTypeAnnotations()
    {
        using (var reader = Db.MySql.Read("SELECT NULL AS a, NULL AS aNull, 'B' AS b, NULL AS bNull, 'C' AS c, NULL AS cNull"))
        {
            Assert.IsTrue(reader.Value.Read());

            var exception = Assert.Throws<ColumnMappingException>(() => MappingDelegateProvider.GetMappingDelegate<HasNullableReferenceTypes>(reader.Value)());
            Assert.That(exception!.Message, Does.Contain("column 0"));
        }

        using (var reader = Db.MySql.Read("SELECT 'A' AS a, NULL AS aNull, NULL AS b, NULL AS bNull, 'C' AS c, NULL AS cNull"))
        {
            Assert.IsTrue(reader.Value.Read());

            var exception = Assert.Throws<ColumnMappingException>(() => MappingDelegateProvider.GetMappingDelegate<HasNullableReferenceTypes>(reader.Value)());
            Assert.That(exception!.Message, Does.Contain("column 2"));
        }

        using (var reader = Db.MySql.Read("SELECT 'A' AS a, NULL AS aNull, 'B' AS b, NULL AS bNull, NULL AS c, NULL AS cNull"))
        {
            Assert.IsTrue(reader.Value.Read());

            var exception = Assert.Throws<ColumnMappingException>(() => MappingDelegateProvider.GetMappingDelegate<HasNullableReferenceTypes>(reader.Value)());
            Assert.That(exception!.Message, Does.Contain("column 4"));
        }

        using (var reader = Db.MySql.Read("SELECT 'A' AS a, NULL AS aNull, 'B' AS b, NULL AS bNull, 'C' AS c, NULL AS cNull"))
        {
            Assert.IsTrue(reader.Value.Read());

            var value = MappingDelegateProvider.GetMappingDelegate<HasNullableReferenceTypes>(reader.Value)();
            Assert.AreEqual(
                ("A", default(string), "B", default(string), "C", default(string)),
                (value.A, value.ANull, value.B, value.BNull, value.C, value.CNull)
            );
        }
    }

    public class HasNullableReferenceTypes
    {
        public HasNullableReferenceTypes(string a, string? aNull) 
        {
            this.A = a;
            this.ANull = aNull;
        }

        public string A { get; }
        public string? ANull { get; }

        public string B { get; set; } = string.Empty;
        public string? BNull { get; set; }

        public string C = string.Empty;
#pragma warning disable CS0649
        public string? CNull;
#pragma warning restore CS0649
    }

    [Test]
    public void TestCanMapToValueTypeWithoutConstructor()
    {
        using var reader = Db.Oracle.Read("SELECT -2 AS a, 10.5 AS b");
        Assert.IsTrue(reader.Value.Read());

        var value = MappingDelegateProvider.GetMappingDelegate<ValueTypeWithoutConstructor>(reader.Value)();
        Assert.AreEqual(-2, value.A);
        Assert.AreEqual(10.5M, value.B);
    }

    private struct ValueTypeWithoutConstructor
    {
        public int A { get; set; }
#pragma warning disable CS0649
        public decimal? B;
#pragma warning restore CS0649
    }

    [Test]
    public void TestCanMapToValueTypeWithConstructor()
    {
        using var reader = Db.Postgres.Read("SELECT 'xyz' AS key, 20 AS value");
        Assert.IsTrue(reader.Value.Read());

        var value = MappingDelegateProvider.GetMappingDelegate<KeyValuePair<string, float>>(reader.Value)();
        Assert.AreEqual("xyz", value.Key);
        Assert.AreEqual(20f, value.Value);
    }

    [Test]
    public void TestCanMapToRecordClass()
    {
        using var reader = Db.SqlServer.Read($"SELECT CAST({long.MinValue} AS BIGINT) AS a, {int.MaxValue} AS b, '?' AS c");
        Assert.IsTrue(reader.Value.Read());

        var value = MappingDelegateProvider.GetMappingDelegate<RecordClass>(reader.Value)();
        Assert.AreEqual(long.MinValue, value.A);
        Assert.AreEqual((uint)int.MaxValue, value.B);
        Assert.AreEqual("?", value.C);
    }

    private record RecordClass(long A, uint B, string C);

    [Test]
    public void TestCanMapToRecordStruct()
    {
        using var reader = Db.SystemDataSqlServer.Read($"SELECT 70.2 AS y, -100.5 AS x");
        Assert.IsTrue(reader.Value.Read());

        var value = MappingDelegateProvider.GetMappingDelegate<RecordStruct>(reader.Value)();
        Assert.AreEqual(-100.5, value.X);
        Assert.AreEqual(70.2, value.Y);
    }

    private readonly record struct RecordStruct(double X, double Y);

    [Test]
    public void TestMustBindToAtLeastOneValue()
    {
        using var reader = Db.MariaDb.Read($"SELECT 1 AS x");
        Assert.IsTrue(reader.Value.Read());

        // todo exception will be different
        Assert.Throws<NotImplementedException>(() => MappingDelegateProvider.GetMappingDelegate<ValueTypeWithoutConstructor>(reader.Value));
    }

    [Test]
    public void TestRejectsPartialBindingIfNotAllColumnsAreUsed()
    {
        using var reader = Db.MySql.Read($"SELECT 1 AS a, '2' AS b, 1.5 AS x");
        Assert.IsTrue(reader.Value.Read());

        var exception = Assert.Throws<MappingException>(() => MappingDelegateProvider.GetMappingDelegate<HasConstructorPropertyAndField>(reader.Value));
        Assert.That(exception!.Message, Does.Contain("The following columns were not bound: 2 (System.Decimal x)"));
    }
}
