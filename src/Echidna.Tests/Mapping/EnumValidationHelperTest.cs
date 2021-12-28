using Medallion.Data.Mapping;
using System.Text.RegularExpressions;
using static Medallion.Data.Tests.Mapping.ScalarConverterTest;

namespace Medallion.Data.Tests.Mapping;

internal class EnumValidationHelperTest
{
    [Test]
    public void TestAdjacentEnum()
    {
        var ranges = GetDefinedRanges(typeof(DateTimeKind));
        CollectionAssert.AreEqual(
            MakeRanges((DateTimeKind.Unspecified, DateTimeKind.Local)),
            ranges
        );
    }

    [Test]
    public void TestAdjacentFlags()
    {
        var flags = GetDefinedFlags(typeof(RegexOptions));
        Assert.AreEqual(Enum.GetValues<RegexOptions>().Aggregate((a, b) => a | b), flags);
    }

    [Test]
    public void TestNonAdjacentRanges()
    {
        var ranges = GetDefinedRanges(typeof(EnumWithNonAdjacentValues));
        CollectionAssert.AreEqual(
            MakeRanges(
                (EnumWithNonAdjacentValues.A, EnumWithNonAdjacentValues.A),
                (EnumWithNonAdjacentValues.B, EnumWithNonAdjacentValues.B),
                (EnumWithNonAdjacentValues.C, EnumWithNonAdjacentValues.E),
                (EnumWithNonAdjacentValues.F, EnumWithNonAdjacentValues.F)
            ),
            ranges
        );
    }

    [Test]
    public void TestNonAdjacentFlags()
    {
        var flags = GetDefinedFlags(typeof(EnumWithNonAdjacentFlags));
        Assert.AreEqual(Enum.GetValues<EnumWithNonAdjacentFlags>().Aggregate((a, b) => a | b), flags);
    }

    [Test]
    public void TestEmptyValues()
    {
        var ranges = GetDefinedRanges(typeof(EmptyEnum));
        Assert.IsEmpty(ranges);
    }

    [Test]
    public void TestEmptyFlags()
    {
        var flags = GetDefinedFlags(typeof(EmptyFlagsEnum));
        Assert.AreEqual(default(EmptyFlagsEnum), flags);
    }

    [Test]
    public void TestSignedFlagsWithWideRange()
    {
        var flags = GetDefinedFlags(typeof(SignedFlagsWithWideRange));
        Assert.AreEqual(Enum.GetValues<SignedFlagsWithWideRange>().Aggregate((a, b) => a | b), flags);
    }

    [Test]
    public void TestUnsignedFlagsWithWideRange()
    {
        var flags = GetDefinedFlags(typeof(UnsignedFlagsWithWideRange));
        Assert.AreEqual(Enum.GetValues<UnsignedFlagsWithWideRange>().Aggregate((a, b) => a | b), flags);
    }

    private static List<(object Start, object End)> GetDefinedRanges(Type type)
    {
        var (ranges, flags) = EnumValidationHelper.GetDefinedValues(type);
        Assert.IsNotNull(ranges); 
        Assert.IsNull(flags);
        return ranges!;
    }

    private static object GetDefinedFlags(Type type)
    {
        var (ranges, flags) = EnumValidationHelper.GetDefinedValues(type);
        Assert.IsNull(ranges);
        Assert.IsNotNull(flags);
        return flags!;
    }

    private static IEnumerable<(object Start, object End)> MakeRanges<TEnum>(params (TEnum Start, TEnum End)[] ranges)
        where TEnum : Enum =>
        ranges.Select(r => ((object)r.Start, (object)r.End));

    [Flags]
    private enum SignedFlagsWithWideRange : sbyte
    {
        A = sbyte.MinValue,
        B = sbyte.MaxValue
    }

    [Flags]
    private enum UnsignedFlagsWithWideRange : byte
    {
        A = byte.MinValue,
        B = byte.MaxValue,
    }
}
