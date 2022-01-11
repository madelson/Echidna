using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Medallion.Data.Mapping;

internal sealed class PocoTypeMappingStrategy : CompositeTypeMappingStrategy
{
    private readonly IReadOnlySet<ParameterOrProperty> _nonNullableReferenceTypeParametersOrProperties;

    public IReadOnlyCollection<ConstructorInfo> Constructors { get; }

    public IReadOnlyDictionary<string, IReadOnlySet<ParameterOrProperty>> NameMapping { get; }

    private PocoTypeMappingStrategy(
        NullabilityInfo? nullabilityInfo,
        IReadOnlyList<ConstructorInfo> constructors,
        IReadOnlyDictionary<string, IReadOnlySet<ParameterOrProperty>> nameMapping)
    {
        this.Constructors = constructors;
        this.NameMapping = nameMapping;
        this._nonNullableReferenceTypeParametersOrProperties = GetNonNullableReferenceTypeParametersOrProperties(
            constructors[0].DeclaringType!,
            nullabilityInfo,
            nameMapping.SelectMany(kvp => kvp.Value)
        );
    }

    public bool IsNonNullableReferenceType(ParameterOrProperty parameterOrProperty) =>
        this._nonNullableReferenceTypeParametersOrProperties.Contains(parameterOrProperty);

    public static bool TryCreatePocoStrategyFor(
        Type type,
        NullabilityInfo? nullabilityInfo,
        [NotNullWhen(returnValue: true)] out CompositeTypeMappingStrategy? strategy,
        [NotNullWhen(returnValue: false)] out string? errorMessage)
    {
        if (type.IsAbstract)
        {
            return Error("An abstract type cannot be mapped to a POCO", out strategy, out errorMessage);
        }

        var constructors = type.GetConstructors(BindingFlags.Public | BindingFlags.Instance)
            .Select(c => (Constructor: c, Parameters: c.GetParameters()))
            .Where(c => !c.Parameters.Any(p => p.Name == null || p.ParameterType.IsByRef))
            .ToArray();
        if (constructors.Length == 0)
        {
            return Error("To be mapped to a POCO a type must have at least one public constructor with no unnamed or by-ref parameters", out strategy, out errorMessage);
        }

        var writableProperties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => (p.SetMethod?.IsPublic ?? false) && !p.GetIndexParameters().Any());

        var nameMapping = constructors.SelectMany(c => c.Parameters)
            .Select(p => (Name: p.Name!, Value: new ParameterOrProperty(p)))
            .Concat(writableProperties.Select(p => (p.Name, Value: new ParameterOrProperty(p))))
            .GroupBy(t => t.Name, t => t.Value, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToHashSet().As<IReadOnlySet<ParameterOrProperty>>(), StringComparer.OrdinalIgnoreCase);
        if (nameMapping.Count == 0)
        {
            return Error("To be mapped to a POCO a type must have at least one public constructor with parameters or at least one writable property", out strategy, out errorMessage);
        }

        return Success(new PocoTypeMappingStrategy(nullabilityInfo, constructors.Select(c => c.Constructor).ToArray(), nameMapping), out strategy, out errorMessage);
    }

    private static IReadOnlySet<ParameterOrProperty> GetNonNullableReferenceTypeParametersOrProperties(
        Type type,
        NullabilityInfo? nullabilityInfo,
        IEnumerable<ParameterOrProperty> parametersOrProperties)
    {
        var result = new HashSet<ParameterOrProperty>();

        var genericArguments = type.GetGenericArguments();
        Type? genericTypeDefinition = null;

        foreach (var parameterOrProperty in parametersOrProperties.Where(p => !p.Type.IsValueType))
        {
            if (parameterOrProperty.Parameter is { } parameter)
            {
                if (Nullability.TryGetFor(parameter, out var info) && info.WriteState == NullabilityState.NotNull)
                {
                    result.Add(parameterOrProperty);
                }
                // if the parameter is a generic argument, then we might be able to get the nullability from NullabilityInfo
                else if (nullabilityInfo != null && genericArguments.Contains(parameter.ParameterType))
                {
                    var genericConstructor = (ConstructorInfo)GenericTypeDefinition().GetMemberWithSameMetadataDefinitionAs(parameter.Member);
                    var genericConstructorParameter = genericConstructor.GetParameters()[parameter.Position];
                    if (genericConstructorParameter.ParameterType.IsGenericTypeParameter
                        && nullabilityInfo.GenericTypeArguments[genericConstructorParameter.ParameterType.GenericParameterPosition].WriteState == NullabilityState.NotNull)
                    {
                        result.Add(parameterOrProperty);
                    }
                }
            }
            else if (Nullability.TryGetFor(parameterOrProperty.Property!, out var info) && info.WriteState == NullabilityState.NotNull)
            {
                result.Add(parameterOrProperty);
            }
            // if the property is a generic argument, then we might be able to get the nullability from NullabilityInfo 
            else if (nullabilityInfo != null && genericArguments.Contains(parameterOrProperty.Type))
            {
                var genericProperty = (PropertyInfo)GenericTypeDefinition().GetMemberWithSameMetadataDefinitionAs(parameterOrProperty.Property!);
                if (genericProperty.PropertyType.IsGenericParameter
                    && nullabilityInfo.GenericTypeArguments[genericProperty.PropertyType.GenericParameterPosition].WriteState == NullabilityState.NotNull)
                {
                    result.Add(parameterOrProperty);
                }
            }
        }

        Type GenericTypeDefinition() => genericTypeDefinition ??= type.GetGenericTypeDefinition();

        return result;
    }
}

internal readonly struct ParameterOrProperty : IEquatable<ParameterOrProperty>
{
    private readonly object _value;

    public ParameterOrProperty(ParameterInfo parameter) { this._value = parameter; }
    public ParameterOrProperty(PropertyInfo property) { this._value = property; }

    public ParameterInfo? Parameter => this._value as ParameterInfo;
    public PropertyInfo? Property => this._value as PropertyInfo;

    public Type Type => 
        this._value is ParameterInfo parameter ? parameter.ParameterType : ((PropertyInfo)this._value).PropertyType;

    public static implicit operator ParameterOrProperty(ParameterInfo parameter) => new(parameter);
    public static implicit operator ParameterOrProperty(PropertyInfo property) => new(property);

    public bool Equals(ParameterOrProperty other) => this._value == other._value;

    public override bool Equals([NotNullWhen(true)] object? obj) => 
        obj is ParameterOrProperty that && this.Equals(that);

    public override int GetHashCode() => this._value.GetHashCode();
}
