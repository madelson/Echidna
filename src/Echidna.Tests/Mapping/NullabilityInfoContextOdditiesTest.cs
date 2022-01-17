using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO.Enumeration;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;

namespace Medallion.Data.Tests.Mapping;

/// <summary>
/// References various issues that make <see cref="NullabilityInfoContext"/> unreliable.
/// </summary>
internal class NullabilityInfoContextOdditiesTest
{
    // https://github.com/dotnet/runtime/issues/63555
    [Test]
    public void TestDoesNotReportCorrectNullabilityForMethodInGenericBaseClass()
    {
        var context = new NullabilityInfoContext();
        var addMethod = typeof(NonNullableValuesDictionary).GetMethod("Add")!;
        var info = context.Create(addMethod.GetParameters()[1]);
        Assert.AreEqual(NullabilityState.Nullable, info.WriteState); // should be NotNull
    }

    private class NonNullableValuesDictionary : Dictionary<string, object> { }

    // replicates https://github.com/dotnet/runtime/issues/63660
    [Test]
    public void TestReportsNullOrNotNullForUnspecifiedGenericParameters()
    {
        var context = new NullabilityInfoContext();

        var fooInfo = context.Create(typeof(UnspecifiedGenericNullableEnabled<string?>).GetConstructors().Single().GetParameters().Single());
        Assert.AreEqual(NullabilityState.NotNull, fooInfo.WriteState); // should be Unknown

        var barInfo = context.Create(typeof(UnspecifiedGenericNullableDisabled<string?>).GetConstructors().Single().GetParameters().Single());
        Assert.AreEqual(NullabilityState.Unknown, barInfo.WriteState); // as expected
    }

    private class UnspecifiedGenericNullableEnabled<T> { public UnspecifiedGenericNullableEnabled(T t) { } }

#nullable disable
    private class UnspecifiedGenericNullableDisabled<T> { public UnspecifiedGenericNullableDisabled(T t) { } }
#nullable enable

    // replicates https://github.com/dotnet/runtime/issues/63846
    [Test]
    public void TestCodeAnalysisAttributeDominance()
    {
        // new HasMultipleCodeAnalysisAttributes().HasAllowAndDisallow = null; // CS8625
        
        var context = new NullabilityInfoContext();
        
        var hasAllowAndDisallowInfo = context.Create(typeof(HasMultipleCodeAnalysisAttributes).GetField("HasAllowAndDisallow")!);
        Assert.AreEqual(NullabilityState.NotNull, hasAllowAndDisallowInfo.ReadState); // as expected
        Assert.AreEqual(NullabilityState.Nullable, hasAllowAndDisallowInfo.WriteState); // should be NotNull
    }

    private class HasMultipleCodeAnalysisAttributes
    {
        [AllowNull, DisallowNull]
        public string HasAllowAndDisallow = string.Empty;
    }

    // replicates https://github.com/dotnet/runtime/issues/63847
    [Test]
    public void TestUnannotatedPrivateMembersRequireFurtherAnalysis()
    {
        var context = new NullabilityInfoContext();
        var directoryInfo = context.Create(typeof(FileSystemEntry).GetProperty("Directory")!);
        Assert.AreEqual(NullabilityState.NotNull, directoryInfo.ReadState);
        Assert.AreEqual(NullabilityState.Unknown, directoryInfo.WriteState); // should be NotNull
    }

    // replicates https://github.com/dotnet/runtime/issues/63848
    [Test]
    public void TestCodeAnalysisAttributesNotRespectedInDisabledContext()
    {
        // new DisabledWithCodeAnalysisAttributes().DisallowNull = null; // CS8625
        // string s = new DisabledWithCodeAnalysisAttributes().MaybeNull; // CS8600

        var context = new NullabilityInfoContext();
        
        var disallowNullInfo = context.Create(typeof(DisabledWithCodeAnalysisAttributes).GetProperty("DisallowNull")!);
        Assert.AreEqual(NullabilityState.Unknown, disallowNullInfo.ReadState);
        Assert.AreEqual(NullabilityState.Unknown, disallowNullInfo.WriteState); // should be NotNull

        var maybeNullInfo = context.Create(typeof(DisabledWithCodeAnalysisAttributes).GetProperty("MaybeNull")!);
        Assert.AreEqual(NullabilityState.Unknown, maybeNullInfo.ReadState); // should be Nullable
        Assert.AreEqual(NullabilityState.Unknown, maybeNullInfo.WriteState);
    }

#nullable disable
    private class DisabledWithCodeAnalysisAttributes
    {
        [DisallowNull] public string DisallowNull { get; set; }
        [MaybeNull] public string MaybeNull { get; set; }
    }
#nullable enable

    // https://github.com/dotnet/runtime/issues/63849
    [Test]
    public void TestIndexerPropertyDoesNotRespectCodeAnalysisAttribute()
    {
        // new HasIndexer()["a"] = null;

        var context = new NullabilityInfoContext();

        var indexerInfo = context.Create(typeof(HasIndexer).GetProperty("Item")!);
        Assert.AreEqual(NullabilityState.NotNull, indexerInfo.WriteState); // should be Nullable
    }

    private class HasIndexer
    {
        [AllowNull] public string this[string? s]
        {
            get => default!;
            set { }
        }
    }

    // replicates https://github.com/dotnet/runtime/issues/63853
    [Test]
    public void TestPrivateConstructorParametersInPublicOnlyAssemblyAreUnknown()
    {
        var context = new NullabilityInfoContext();
        var constructor = typeof(IndexOutOfRangeException)
            .GetConstructor(BindingFlags.NonPublic | BindingFlags.Instance, new[] { typeof(SerializationInfo), typeof(StreamingContext) })!;
        var info = context.Create(constructor.GetParameters()[0]);
        Assert.AreEqual(NullabilityState.Nullable, info.WriteState); // should be Unknown
    }

    // copied from https://github.com/graphql-dotnet/graphql-dotnet/blob/f190a1566f61f29e1909fe6b7dcbb7140c404908/src/GraphQL.Tests/NRTTests.cs
    [Test]
    public void TestNRTField2()
    {
        var type = typeof(NullableTestClass);
        var field = type.GetMethod("Field2")!;
        var returnParameter = field.ReturnParameter;
        var context = new NullabilityInfoContext();
        var info = context.Create(returnParameter);

        //test 1
        Assert.AreEqual(typeof(Tuple<Tuple<string, string>, string>), info.Type);
        Assert.AreEqual(NullabilityState.NotNull, info.ReadState);
        Assert.AreEqual(2, info.GenericTypeArguments.Length);
        Assert.AreEqual(2, info.GenericTypeArguments.Length);

        //test 2
        Assert.AreEqual(typeof(Tuple<string, string>), info.GenericTypeArguments[0].Type);
        Assert.AreEqual(NullabilityState.NotNull, info.GenericTypeArguments[0].ReadState);
        Assert.AreEqual(2, info.GenericTypeArguments[0].GenericTypeArguments.Length);

        //test 3
        Assert.AreEqual(typeof(string), info.GenericTypeArguments[0].GenericTypeArguments[0].Type);
        Assert.AreEqual(NullabilityState.Nullable, info.GenericTypeArguments[0].GenericTypeArguments[0].ReadState);

        //test 4
        Assert.AreEqual(typeof(string), info.GenericTypeArguments[0].GenericTypeArguments[1].Type);
        Assert.AreEqual(NullabilityState.Nullable, info.GenericTypeArguments[0].GenericTypeArguments[1].ReadState);

        //test 5
        Assert.AreEqual(typeof(string), info.GenericTypeArguments[1].Type);
        Assert.AreEqual(NullabilityState.Nullable, info.GenericTypeArguments[1].ReadState); // should be NotNull
    }

    public class NullableTestClass
    {
        public static Tuple<Tuple<string?, string?>, string> Field2() => null!;
        /*             1      2      3        4         5
         *
         * 1: Tuple<Tuple<string, string>, string>
         *    non-null
         *
         * 2: Tuple<string, string>
         *    non-null
         *
         * 3: string
         *    nullable
         *
         * 4: string
         *    nullable
         *
         * 5: string
         *    non-null
         */
    }
}
