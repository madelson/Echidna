using Medallion.Data.Mapping;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Medallion.Data.Tests.Mapping;

internal class DictionaryTypeMappingStrategyTest
{
    [Test]
    public void TestCanGetIReadOnlyDictionaryStrategy()
    {
        var strategy = CheckStrategy(typeof(IReadOnlyDictionary<string, object>));
        Assert.AreEqual(typeof(Dictionary<string, object>), strategy!.Constructor.DeclaringType);
        CheckStrategy(typeof(IReadOnlyDictionary<string, int?>));
        CheckStrategy(typeof(IReadOnlyDictionary<int, object>), expectedErrorMessage: "<string, V>");
    }

    [Test]
    public void TestCanGetIDictionaryStrategy()
    {
        var strategy = CheckStrategy(typeof(IDictionary<string, object>));
        Assert.AreEqual(typeof(Dictionary<string, object>), strategy!.Constructor.DeclaringType);
        CheckStrategy(typeof(IDictionary<string, int?>));
        CheckStrategy(typeof(IDictionary<int, object>), expectedErrorMessage: "<string, V>");
    }

    [Test]
    public void TestCanGetNonGenericDictionaryStrategy()
    {
        var strategy = CheckStrategy(typeof(IDictionary));
        Assert.AreEqual(typeof(Dictionary<string, object>), strategy!.Constructor.DeclaringType);
    }

    [Test]
    public void TestCannotGetOtherInterfaces()
    {
        CheckStrategy(typeof(IEnumerable<KeyValuePair<string, object>>), expectedErrorMessage: "Only interfaces");
        CheckStrategy(typeof(IEnumerable), expectedErrorMessage: "Only interfaces");
    }

    [Test]
    public void TestCanGetDictionaryStrategy() => CheckStrategy(typeof(Dictionary<string, object>));

    [Test]
    public void TestCanGetSortedDictionaryStrategy() => CheckStrategy(typeof(SortedDictionary<string, object>), expectCapacity: false);

    [Test]
    public void TestCannotGetAbstractTypeStrategy() =>
        CheckStrategy(typeof(AbstractDictionary), expectedErrorMessage: "abstract type cannot be mapped");

    private abstract class AbstractDictionary : Dictionary<string, object> { }

    [Test]
    public void TestCannotGetNonDictionaryType() =>
        CheckStrategy(new { a = 2 }.GetType(), expectedErrorMessage: "must implement IDictionary");

    [Test]
    public void TestCannotGetAmbiguousDictionaryType() =>
        CheckStrategy(typeof(AmbiguousDictionary), expectedErrorMessage: "implements IDictionary<string, V> for multiple types V");

    private class AmbiguousDictionary : Dictionary<string, object>, IDictionary<string, int>
    {
        #region Implementation
        int IDictionary<string, int>.this[string key] { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
        ICollection<string> IDictionary<string, int>.Keys => throw new NotImplementedException();
        ICollection<int> IDictionary<string, int>.Values => throw new NotImplementedException();
        bool ICollection<KeyValuePair<string, int>>.IsReadOnly => throw new NotImplementedException();
        void IDictionary<string, int>.Add(string key, int value) => throw new NotImplementedException();
        void ICollection<KeyValuePair<string, int>>.Add(KeyValuePair<string, int> item) => throw new NotImplementedException();
        bool ICollection<KeyValuePair<string, int>>.Contains(KeyValuePair<string, int> item) => throw new NotImplementedException();
        void ICollection<KeyValuePair<string, int>>.CopyTo(KeyValuePair<string, int>[] array, int arrayIndex) => throw new NotImplementedException();
        IEnumerator<KeyValuePair<string, int>> IEnumerable<KeyValuePair<string, int>>.GetEnumerator() => throw new NotImplementedException();
        bool ICollection<KeyValuePair<string, int>>.Remove(KeyValuePair<string, int> item) => throw new NotImplementedException();
        bool IDictionary<string, int>.TryGetValue(string key, out int value) => throw new NotImplementedException();
        #endregion
    }

    [Test]
    public void TestPrefersComparerOverCapacity() => CheckStrategy(typeof(ComparerOrCapacityDictionary), expectCapacity: false);

    private class ComparerOrCapacityDictionary : Dictionary<string, object>
    {
        public ComparerOrCapacityDictionary(IEqualityComparer<string> comparer) : base(comparer) { }
        public ComparerOrCapacityDictionary(int capacity) : base(capacity) { }
    }

    [Test]
    public void TestAllowsDefaultedParameters()
    {
        var strategy = CheckStrategy(typeof(DefaultedParameterDictionary), expectCapacity: false, expectComparer: false);
        Assert.AreEqual(DictionaryTypeMappingStrategy.ParameterKind.Defaulted, strategy!.ConstructorParameters.Single().Kind);
    }

    private class DefaultedParameterDictionary : Dictionary<string, byte>
    {
        public DefaultedParameterDictionary(int x = 10) { }
    }

    [Test]
    public void TestMustFindUsableConstructor() =>
        CheckStrategy(typeof(NoUsableConstructorDictionary), expectedErrorMessage: "must have a public constructor");

    private class NoUsableConstructorDictionary : Dictionary<string, object> { private NoUsableConstructorDictionary(int x) { } }

    [Test]
    public void TestCanFindExplicitlyImplementedAddMethod()
    {
        var strategy = CheckStrategy(typeof(ExpandoObject), expectCapacity: false, expectComparer: false);
        Assert.IsFalse(strategy!.AddMethod.IsPublic);
    }

    [Test]
    public void TestCanDetermineDictionaryPropertyNullability()
    {
        var nullableProperty = typeof(HasDictionaries).GetProperty("Nullable")!;
        Assert.IsTrue(Nullability.TryGetFor(nullableProperty, out var nullableNullability));
        CheckStrategy(typeof(Dictionary<string, string>), nullableNullability);

        var nonNullableProperty = typeof(HasDictionaries).GetProperty("NonNullable")!;
        Assert.IsTrue(Nullability.TryGetFor(nonNullableProperty, out var nonNullableNullability));
        CheckStrategy(typeof(Dictionary<string, string>), nonNullableNullability, expectNonNullableReferenceType: true);
    }

    private class HasDictionaries
    {
        public Dictionary<string, string?>? Nullable { get; set; }
        public Dictionary<string, string>? NonNullable { get; set; }
    }

    // see https://github.com/dotnet/runtime/issues/63555
    [Test]
    public void TestCannotDetermineInheritedNullability() =>
        CheckStrategy(typeof(NonNullableValuesDictionary), expectNonNullableReferenceType: false, expectCapacity: false, expectComparer: false);

    private class NonNullableValuesDictionary : Dictionary<string, object> { }

    private static DictionaryTypeMappingStrategy? CheckStrategy(
        Type type, 
        NullabilityInfo? nullabilityInfo = null, 
        string? expectedErrorMessage = null,
        bool expectCapacity = true,
        bool expectComparer = true,
        bool expectNonNullableReferenceType = false)
    {
        if (DictionaryTypeMappingStrategy.TryCreateDictionaryStrategyFor(type, nullabilityInfo, out var strategy, out var errorMessage))
        {
            Assert.IsNull(expectedErrorMessage);
            var dictionaryStrategy = (DictionaryTypeMappingStrategy)strategy;
            AssertIsValid(dictionaryStrategy);
            Assert.AreEqual(expectCapacity, dictionaryStrategy.ConstructorParameters.Any(t => t.Kind == DictionaryTypeMappingStrategy.ParameterKind.Capacity));
            Assert.AreEqual(expectComparer, dictionaryStrategy.ConstructorParameters.Any(t => t.Kind == DictionaryTypeMappingStrategy.ParameterKind.Comparer));
            Assert.AreEqual(expectNonNullableReferenceType, dictionaryStrategy.IsValueTypeNonNullableReferenceType);
            return dictionaryStrategy;
        }

        Assert.IsNotNull(expectedErrorMessage);
        Assert.That(errorMessage, Does.Contain(expectedErrorMessage));
        return null;
    }

    private static void AssertIsValid(DictionaryTypeMappingStrategy strategy)
    {
        Assert.IsNotNull(strategy.ValueType);
        Assert.IsNotNull(strategy.Constructor);
        Assert.IsNotNull(strategy.ConstructorParameters);
        Assert.IsNotNull(strategy.AddMethod);

        Assert.AreEqual(strategy.ValueType, strategy.AddMethod.GetParameters()[1].ParameterType);

        Assert.IsFalse(strategy.ValueType.IsValueType && strategy.IsValueTypeNonNullableReferenceType);

        Assert.IsTrue(strategy.AddMethod.DeclaringType!.IsAssignableFrom(strategy.Constructor.DeclaringType));

        CollectionAssert.AreEqual(strategy.Constructor.GetParameters(), strategy.ConstructorParameters.Select(c => c.Parameter));

        Assert.AreEqual(typeof(string), strategy.AddMethod.GetParameters()[0].ParameterType);
    }
}
