﻿using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Medallion.Data.Mapping;

internal sealed class PocoTypeMappingStrategy : CompositeTypeMappingStrategy
{
    private readonly IReadOnlySet<BindableMember> _nonNullableReferenceTypeParametersOrProperties;

    public Type PocoType { get; }

    public IReadOnlyCollection<ConstructorInfo> Constructors { get; }

    public IReadOnlyDictionary<string, IReadOnlySet<BindableMember>> NameMapping { get; }

    private PocoTypeMappingStrategy(
        Type pocoType,
        NullabilityInfo? nullabilityInfo,
        IReadOnlyList<ConstructorInfo> constructors,
        IReadOnlyDictionary<string, IReadOnlySet<BindableMember>> nameMapping)
    {
        this.PocoType = pocoType;
        this.Constructors = constructors;
        this.NameMapping = nameMapping;
        this._nonNullableReferenceTypeParametersOrProperties = GetNonNullableReferenceTypeParametersOrProperties(
            pocoType,
            nullabilityInfo,
            nameMapping.SelectMany(kvp => kvp.Value)
        );
    }

    public bool IsNonNullableReferenceType(BindableMember parameterOrProperty) =>
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
        if (constructors.Length == 0 && !type.IsValueType)
        {
            return Error("To be mapped to a POCO, a reference type must have at least one public constructor with no unnamed or by-ref parameters", out strategy, out errorMessage);
        }

        // TODO support fields
        var writableProperties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => (p.SetMethod?.IsPublic ?? false) && !p.GetIndexParameters().Any());
        var writableFields = type.GetFields(BindingFlags.Public | BindingFlags.Instance)
            .Where(f => !f.IsInitOnly);

        var nameMapping = constructors.SelectMany(c => c.Parameters)
            .Select(p => (Name: p.Name!, Value: new BindableMember(p)))
            .Concat(writableProperties.Select(p => (p.Name, Value: new BindableMember(p))))
            .Concat(writableFields.Select(f => (f.Name, Value: new BindableMember(f))))
            .GroupBy(t => t.Name, t => t.Value, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToHashSet().As<IReadOnlySet<BindableMember>>(), StringComparer.OrdinalIgnoreCase);
        if (nameMapping.Count == 0)
        {
            return Error("To be mapped to a POCO a type must have at least one public constructor with parameters or at least one public writable property or field", out strategy, out errorMessage);
        }

        return Success(new PocoTypeMappingStrategy(type, nullabilityInfo, constructors.Select(c => c.Constructor).ToArray(), nameMapping), out strategy, out errorMessage);
    }

    private static IReadOnlySet<BindableMember> GetNonNullableReferenceTypeParametersOrProperties(
        Type type,
        NullabilityInfo? nullabilityInfo,
        IEnumerable<BindableMember> parametersOrProperties)
    {
        // TODO this will have to be revisited wrt generic types; see https://github.com/dotnet/runtime/issues/63555

        var result = new HashSet<BindableMember>();

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

internal readonly struct BindableMember : IEquatable<BindableMember>
{
    private readonly object _value;

    public BindableMember(ParameterInfo parameter) { this._value = parameter; }
    public BindableMember(PropertyInfo property) { this._value = property; }
    public BindableMember(FieldInfo field) { this._value = field; }

    public ParameterInfo? Parameter => this._value as ParameterInfo;
    public PropertyInfo? Property => this._value as PropertyInfo;
    public FieldInfo? Field => this._value as FieldInfo;

    public Type Type => 
        this._value is ParameterInfo parameter ? parameter.ParameterType 
            : this._value is PropertyInfo property ? property.PropertyType
            : ((FieldInfo)this._value).FieldType;

    public static implicit operator BindableMember(ParameterInfo parameter) => new(parameter);
    public static implicit operator BindableMember(PropertyInfo property) => new(property);
    public static implicit operator BindableMember(FieldInfo field) => new(field);

    public bool Equals(BindableMember other) => this._value == other._value;

    public override bool Equals([NotNullWhen(true)] object? obj) => 
        obj is BindableMember that && this.Equals(that);

    public override int GetHashCode() => this._value.GetHashCode();
}
