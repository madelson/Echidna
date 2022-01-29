using Medallion.Data.Mapping;
using System.Data.Common;
using System.Reflection;

namespace Medallion.Data.Tests.Mapping;

internal class PocoTypeMappingStrategyTest
{
    [Test]
    public void TestCannotGetAbstractOrInterfaceStrategy()
    {
        CheckStrategy(typeof(IDisposable), expectedErrorMessage: "abstract");
        CheckStrategy(typeof(DbConnection), expectedErrorMessage: "abstract");
    }

    [Test]
    public void TestRequiresPublicConstructor() =>
        CheckStrategy(typeof(InternalConstructorOnly), expectedErrorMessage: "at least one public constructor");

    private class InternalConstructorOnly { internal InternalConstructorOnly(int a) { } }

    [Test]
    public void TestRequiresAtLeastOneBindableName() =>
        CheckStrategy(typeof(NothingToBind), expectedErrorMessage: "at least one public constructor with parameters or at least one public writable property or field");

    private class NothingToBind 
    {
        public NothingToBind() { }
        public NothingToBind(ref int i) { }
        public NothingToBind(in byte b) { }
        public NothingToBind(out char c) { c = 'a'; }

        public string this[int a] => a.ToString(); 
        public int PrivateSet { get; private set; }
        public int NoSet { get; } = 2;
    }

    [Test]
    public void TestCanBind()
    {
        var strategy = CheckStrategy(typeof(Bindable))!;
        
        Assert.AreEqual(3, strategy.Constructors.Count);
        Assert.AreEqual(2, strategy.NameMapping["A"].Count);
        Assert.AreEqual(2, strategy.NameMapping["b"].Count);
        Assert.AreEqual(1, strategy.NameMapping["C"].Count);
        Assert.AreEqual(3, strategy.NameMapping.Count);

        Assert.IsTrue(strategy.IsNonNullableReferenceType(strategy.NameMapping["A"].Single(p => p.Parameter != null)));
        Assert.IsTrue(strategy.IsNonNullableReferenceType(strategy.NameMapping["B"].Single(p => p.Property != null)));
        Assert.IsFalse(strategy.IsNonNullableReferenceType(strategy.NameMapping["C"].Single(p => p.Parameter != null)));
    }

    private class Bindable
    {
        public Bindable() { }

        public Bindable(string a, int b) { }

        public Bindable(string? c) { }

        public int A { get; init; }
        public string B { set { } }
    }

    [Test]
    public void TestSupportsConstructorlessStructWithWritableProperties()
    {
        CheckStrategy(typeof(ConstructorlessStructNoProperties), expectedErrorMessage: "must have at least one public constructor");

        var strategy = CheckStrategy(typeof(ConstructorlessStruct))!;
        Assert.IsEmpty(strategy.Constructors);
        Assert.AreEqual(1, strategy.NameMapping.Count);
        Assert.AreEqual(1, strategy.NameMapping["A"].Count);
    }

    private struct ConstructorlessStructNoProperties { }

    private struct ConstructorlessStruct { public int A; }

    // TODO revisit this after working through https://github.com/dotnet/runtime/issues/63555
    //[Test]
    //public void TestCanGetContextualNullabilityInfoForGenerics()
    //{
    //    var propertyA = typeof(HasGeneric).GetProperty("A")!;
    //    Assert.IsTrue(Nullability.TryGetFor(propertyA, out var infoA));
    //    var strategy = CheckStrategy(propertyA.PropertyType, infoA)!;
    //    Assert.IsFalse(strategy.IsNonNullableReferenceType(strategy.NameMapping["Param"].Single()));
    //    Assert.IsTrue(strategy.IsNonNullableReferenceType(strategy.NameMapping["Prop"].Single()));

    //    var propertyB = typeof(HasGeneric).GetProperty("B")!;
    //    Assert.IsTrue(Nullability.TryGetFor(propertyB, out var infoB));
    //    strategy = CheckStrategy(propertyB.PropertyType, infoB)!;
    //    Assert.IsTrue(strategy.IsNonNullableReferenceType(strategy.NameMapping["Param"].Single()));
    //    Assert.IsFalse(strategy.IsNonNullableReferenceType(strategy.NameMapping["Prop"].Single()));
    //}

    //private class HasGeneric
    //{
    //    public Generic<string?, object> A { get; set; }
    //    public Generic<string, object?> B { get; set; }
    //}

    //private class Generic<T, V>
    //{
    //    public Generic(T param) { }

    //    public V Prop { get; set; } = default!;
    //}

    private static PocoTypeMappingStrategy? CheckStrategy(
        Type type,
        NullabilityInfo? nullabilityInfo = null,
        string? expectedErrorMessage = null)
    {
        if (PocoTypeMappingStrategy.TryCreatePocoStrategyFor(type, nullabilityInfo, out var strategy, out var errorMessage))
        {
            Assert.IsNull(expectedErrorMessage);
            var pocoStrategy = (PocoTypeMappingStrategy)strategy;
            AssertIsValid(pocoStrategy);
            return pocoStrategy;
        }

        Assert.IsNotNull(expectedErrorMessage);
        Assert.That(errorMessage, Does.Contain(expectedErrorMessage));
        return null;
    }

    private static void AssertIsValid(PocoTypeMappingStrategy strategy)
    {
        if (!strategy.PocoType.IsValueType) { Assert.IsNotEmpty(strategy.Constructors); }
        Assert.IsNotEmpty(strategy.NameMapping);
        foreach (var set in strategy.NameMapping.Values)
        {
            Assert.IsNotEmpty(set);
        }
    }
}
