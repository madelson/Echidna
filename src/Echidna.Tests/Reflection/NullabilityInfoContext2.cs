using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Medallion.Data.Tests.Mapping;

using NullabilityState = NullabilityState2;

public sealed class NullabilityInfoContext2
{
    private const string CompilerServicesNameSpace = "System.Runtime.CompilerServices";
    private readonly Dictionary<Module, NotAnnotatedStatus> _publicOnlyModules = new();
    private readonly Dictionary<MemberInfo, NullabilityState> _context = new();

    internal static bool IsSupported { get; } =
        AppContext.TryGetSwitch("System.Reflection.NullabilityInfoContext.IsSupported", out bool isSupported) ? isSupported : true;

    [Flags]
    private enum NotAnnotatedStatus
    {
        None = 0x0,    // no restriction, all members annotated
        Private = 0x1, // private members not annotated
        Internal = 0x2 // internal members not annotated
    }

    private NullabilityState GetNullableContext(MemberInfo? memberInfo)
    {
        while (memberInfo != null)
        {
            if (_context.TryGetValue(memberInfo, out NullabilityState state))
            {
                return state;
            }

            foreach (CustomAttributeData attribute in memberInfo.GetCustomAttributesData())
            {
                if (attribute.AttributeType.Name == "NullableContextAttribute" &&
                    attribute.AttributeType.Namespace == CompilerServicesNameSpace &&
                    attribute.ConstructorArguments.Count == 1)
                {
                    state = TranslateByte(attribute.ConstructorArguments[0].Value);
                    _context.Add(memberInfo, state);
                    return state;
                }
            }

            memberInfo = memberInfo.DeclaringType;
        }

        return NullabilityState.Unknown;
    }

    /// <summary>
    /// Populates <see cref="NullabilityInfo" /> for the given <see cref="ParameterInfo" />.
    /// If the nullablePublicOnly feature is set for an assembly, like it does in .NET SDK, the private and/or internal member's
    /// nullability attributes are omitted, in this case the API will return NullabilityState.Unknown state.
    /// </summary>
    /// <param name="parameterInfo">The parameter which nullability info gets populated</param>
    /// <exception cref="ArgumentNullException">If the parameterInfo parameter is null</exception>
    /// <returns><see cref="NullabilityInfo" /></returns>
    public NullabilityInfo2 Create(ParameterInfo parameterInfo)
    {
        if (parameterInfo is null)
        {
            throw new ArgumentNullException(nameof(parameterInfo));
        }

        EnsureIsSupported();

        if (parameterInfo.Member is MethodInfo method && IsPrivateOrInternalMethodAndAnnotationDisabled(method))
        {
            return new NullabilityInfo2(parameterInfo.ParameterType, NullabilityState.Unknown, NullabilityState.Unknown, null, Array.Empty<NullabilityInfo2>());
        }

        IList<CustomAttributeData> attributes = parameterInfo.GetCustomAttributesData();
        NullabilityInfo2 nullability = GetNullabilityInfo(parameterInfo.Member, parameterInfo.ParameterType, attributes);

        if (nullability.ReadState != NullabilityState.Unknown)
        {
            CheckParameterMetadataType(parameterInfo, nullability);
        }

        CheckNullabilityAttributes(nullability, attributes);
        return nullability;
    }

    private void CheckParameterMetadataType(ParameterInfo parameter, NullabilityInfo2 nullability)
    {
        if (parameter.Member is MethodInfo method)
        {
            MethodInfo metaMethod = GetMethodMetadataDefinition(method);
            ParameterInfo? metaParameter = null;
            if (string.IsNullOrEmpty(parameter.Name))
            {
                metaParameter = metaMethod.ReturnParameter;
            }
            else
            {
                ParameterInfo[] parameters = metaMethod.GetParameters();
                for (int i = 0; i < parameters.Length; i++)
                {
                    if (parameter.Position == i &&
                        parameter.Name == parameters[i].Name)
                    {
                        metaParameter = parameters[i];
                        break;
                    }
                }
            }

            if (metaParameter != null)
            {
                if (method != metaMethod 
                    && metaParameter.ParameterType.IsGenericTypeParameter
                    && method.ReflectedType != null
                    && (
                        !method.ReflectedType.IsConstructedGenericType
                        || method.ReflectedType.GetGenericTypeDefinition() != metaMethod.DeclaringType
                    ))
                {
                }

                CheckGenericParameters(nullability, metaMethod, metaParameter.ParameterType, method);
            }
        }
    }

    private static MethodInfo GetMethodMetadataDefinition(MethodInfo method)
    {
        if (method.IsGenericMethod && !method.IsGenericMethodDefinition)
        {
            method = method.GetGenericMethodDefinition();
        }

        return (MethodInfo)GetMemberMetadataDefinition(method);
    }

    private void CheckNullabilityAttributes(NullabilityInfo2 nullability, IList<CustomAttributeData> attributes)
    {
        foreach (CustomAttributeData attribute in attributes)
        {
            if (attribute.AttributeType.Namespace == "System.Diagnostics.CodeAnalysis")
            {
                if (attribute.AttributeType.Name == "NotNullAttribute" &&
                    nullability.ReadState == NullabilityState.Nullable)
                {
                    nullability.ReadState = NullabilityState.NotNull;
                    break;
                }
                else if ((attribute.AttributeType.Name == "MaybeNullAttribute" ||
                        attribute.AttributeType.Name == "MaybeNullWhenAttribute") &&
                        nullability.ReadState == NullabilityState.NotNull &&
                        !nullability.Type.IsValueType)
                {
                    nullability.ReadState = NullabilityState.Nullable;
                    break;
                }

                if (attribute.AttributeType.Name == "DisallowNullAttribute" &&
                    nullability.WriteState == NullabilityState.Nullable)
                {
                    nullability.WriteState = NullabilityState.NotNull;
                    break;
                }
                else if (attribute.AttributeType.Name == "AllowNullAttribute" &&
                    nullability.WriteState == NullabilityState.NotNull &&
                    !nullability.Type.IsValueType)
                {
                    nullability.WriteState = NullabilityState.Nullable;
                    break;
                }
            }
        }
    }

    /// <summary>
    /// Populates <see cref="NullabilityInfo" /> for the given <see cref="PropertyInfo" />.
    /// If the nullablePublicOnly feature is set for an assembly, like it does in .NET SDK, the private and/or internal member's
    /// nullability attributes are omitted, in this case the API will return NullabilityState.Unknown state.
    /// </summary>
    /// <param name="propertyInfo">The parameter which nullability info gets populated</param>
    /// <exception cref="ArgumentNullException">If the propertyInfo parameter is null</exception>
    /// <returns><see cref="NullabilityInfo" /></returns>
    public NullabilityInfo2 Create(PropertyInfo propertyInfo)
    {
        if (propertyInfo is null)
        {
            throw new ArgumentNullException(nameof(propertyInfo));
        }

        EnsureIsSupported();

        NullabilityInfo2 nullability = GetNullabilityInfo(propertyInfo, propertyInfo.PropertyType, propertyInfo.GetCustomAttributesData());
        MethodInfo? getter = propertyInfo.GetGetMethod(true);
        MethodInfo? setter = propertyInfo.GetSetMethod(true);

        if (getter != null)
        {
            if (IsPrivateOrInternalMethodAndAnnotationDisabled(getter))
            {
                nullability.ReadState = NullabilityState.Unknown;
            }

            CheckNullabilityAttributes(nullability, getter.ReturnParameter.GetCustomAttributesData());
        }
        else
        {
            nullability.ReadState = NullabilityState.Unknown;
        }

        if (setter != null)
        {
            if (IsPrivateOrInternalMethodAndAnnotationDisabled(setter))
            {
                nullability.WriteState = NullabilityState.Unknown;
            }

            CheckNullabilityAttributes(nullability, setter.GetParameters()[0].GetCustomAttributesData());
        }
        else
        {
            nullability.WriteState = NullabilityState.Unknown;
        }

        return nullability;
    }

    private bool IsPrivateOrInternalMethodAndAnnotationDisabled(MethodInfo method)
    {
        if ((method.IsPrivate || method.IsFamilyAndAssembly || method.IsAssembly) &&
           IsPublicOnly(method.IsPrivate, method.IsFamilyAndAssembly, method.IsAssembly, method.Module))
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Populates <see cref="NullabilityInfo" /> for the given <see cref="EventInfo" />.
    /// If the nullablePublicOnly feature is set for an assembly, like it does in .NET SDK, the private and/or internal member's
    /// nullability attributes are omitted, in this case the API will return NullabilityState.Unknown state.
    /// </summary>
    /// <param name="eventInfo">The parameter which nullability info gets populated</param>
    /// <exception cref="ArgumentNullException">If the eventInfo parameter is null</exception>
    /// <returns><see cref="NullabilityInfo" /></returns>
    public NullabilityInfo2 Create(EventInfo eventInfo)
    {
        if (eventInfo is null)
        {
            throw new ArgumentNullException(nameof(eventInfo));
        }

        EnsureIsSupported();

        return GetNullabilityInfo(eventInfo, eventInfo.EventHandlerType!, eventInfo.GetCustomAttributesData());
    }

    /// <summary>
    /// Populates <see cref="NullabilityInfo" /> for the given <see cref="FieldInfo" />
    /// If the nullablePublicOnly feature is set for an assembly, like it does in .NET SDK, the private and/or internal member's
    /// nullability attributes are omitted, in this case the API will return NullabilityState.Unknown state.
    /// </summary>
    /// <param name="fieldInfo">The parameter which nullability info gets populated</param>
    /// <exception cref="ArgumentNullException">If the fieldInfo parameter is null</exception>
    /// <returns><see cref="NullabilityInfo" /></returns>
    public NullabilityInfo2 Create(FieldInfo fieldInfo)
    {
        if (fieldInfo is null)
        {
            throw new ArgumentNullException(nameof(fieldInfo));
        }

        EnsureIsSupported();

        if (IsPrivateOrInternalFieldAndAnnotationDisabled(fieldInfo))
        {
            return new NullabilityInfo2(fieldInfo.FieldType, NullabilityState.Unknown, NullabilityState.Unknown, null, Array.Empty<NullabilityInfo2>());
        }

        IList<CustomAttributeData> attributes = fieldInfo.GetCustomAttributesData();
        NullabilityInfo2 nullability = GetNullabilityInfo(fieldInfo, fieldInfo.FieldType, attributes);
        CheckNullabilityAttributes(nullability, attributes);
        return nullability;
    }

    private static void EnsureIsSupported()
    {
        if (!IsSupported)
        {
            throw new InvalidOperationException("not supported");
        }
    }

    private bool IsPrivateOrInternalFieldAndAnnotationDisabled(FieldInfo fieldInfo)
    {
        if ((fieldInfo.IsPrivate || fieldInfo.IsFamilyAndAssembly || fieldInfo.IsAssembly) &&
            IsPublicOnly(fieldInfo.IsPrivate, fieldInfo.IsFamilyAndAssembly, fieldInfo.IsAssembly, fieldInfo.Module))
        {
            return true;
        }

        return false;
    }

    private bool IsPublicOnly(bool isPrivate, bool isFamilyAndAssembly, bool isAssembly, Module module)
    {
        if (!_publicOnlyModules.TryGetValue(module, out NotAnnotatedStatus value))
        {
            value = PopulateAnnotationInfo(module.GetCustomAttributesData());
            _publicOnlyModules.Add(module, value);
        }

        if (value == NotAnnotatedStatus.None)
        {
            return false;
        }

        if ((isPrivate || isFamilyAndAssembly) && value.HasFlag(NotAnnotatedStatus.Private) ||
             isAssembly && value.HasFlag(NotAnnotatedStatus.Internal))
        {
            return true;
        }

        return false;
    }

    private NotAnnotatedStatus PopulateAnnotationInfo(IList<CustomAttributeData> customAttributes)
    {
        foreach (CustomAttributeData attribute in customAttributes)
        {
            if (attribute.AttributeType.Name == "NullablePublicOnlyAttribute" &&
                attribute.AttributeType.Namespace == CompilerServicesNameSpace &&
                attribute.ConstructorArguments.Count == 1)
            {
                if (attribute.ConstructorArguments[0].Value is bool boolValue && boolValue)
                {
                    return NotAnnotatedStatus.Internal | NotAnnotatedStatus.Private;
                }
                else
                {
                    return NotAnnotatedStatus.Private;
                }
            }
        }

        return NotAnnotatedStatus.None;
    }

    private NullabilityInfo2 GetNullabilityInfo(MemberInfo memberInfo, Type type, IList<CustomAttributeData> customAttributes) =>
        GetNullabilityInfo(memberInfo, type, customAttributes, 0);

    private NullabilityInfo2 GetNullabilityInfo(MemberInfo memberInfo, Type type, IList<CustomAttributeData> customAttributes, int index)
    {
        NullabilityState state = NullabilityState.Unknown;
        NullabilityInfo2? elementState = null;
        NullabilityInfo2[] genericArgumentsState = Array.Empty<NullabilityInfo2>();
        Type? underlyingType = type;

        if (type.IsValueType)
        {
            underlyingType = Nullable.GetUnderlyingType(type);

            if (underlyingType != null)
            {
                state = NullabilityState.Nullable;
            }
            else
            {
                underlyingType = type;
                state = NullabilityState.NotNull;
            }
        }
        else
        {
            if (!ParseNullableState(customAttributes, index, ref state))
            {
                state = GetNullableContext(memberInfo);
            }
            
            if (type.IsGenericParameter
                && !type.GenericParameterAttributes.HasFlag(GenericParameterAttributes.ReferenceTypeConstraint))
            {
                if (state == NullabilityState.Nullable)
                {
                    state = NullabilityState.NullableIfGenericArgumentIsNonNullableReferenceType;
                }
                else if (state == NullabilityState.NotNull)
                {
                    state = NullabilityState.NotNullIfGenericArgumentIsNonNullableReferenceType;
                }
            }
            else if (type.IsArray)
            {
                elementState = GetNullabilityInfo(memberInfo, type.GetElementType()!, customAttributes, index + 1);
            }
        }

        if (underlyingType.IsGenericType)
        {
            Type[] genericArguments = underlyingType.GetGenericArguments();
            genericArgumentsState = new NullabilityInfo2[genericArguments.Length];

            for (int i = 0, offset = 0; i < genericArguments.Length; i++)
            {
                Type t = Nullable.GetUnderlyingType(genericArguments[i]) ?? genericArguments[i];

                if (!t.IsValueType || t.IsGenericType)
                {
                    offset++;
                }

                genericArgumentsState[i] = GetNullabilityInfo(memberInfo, genericArguments[i], customAttributes, index + offset);
            }
        }

        NullabilityInfo2 nullability = new NullabilityInfo2(type, state, state, elementState, genericArgumentsState);

        if (!type.IsValueType && state != NullabilityState.Unknown)
        {
            TryLoadGenericMetaTypeNullability(memberInfo, nullability);
        }

        return nullability;
    }

    private static bool ParseNullableState(IList<CustomAttributeData> customAttributes, int index, ref NullabilityState state)
    {
        foreach (CustomAttributeData attribute in customAttributes)
        {
            if (attribute.AttributeType.Name == "NullableAttribute" &&
                attribute.AttributeType.Namespace == CompilerServicesNameSpace &&
                attribute.ConstructorArguments.Count == 1)
            {
                object? o = attribute.ConstructorArguments[0].Value;

                if (o is byte b)
                {
                    state = TranslateByte(b);
                    return true;
                }
                else if (o is ReadOnlyCollection<CustomAttributeTypedArgument> args &&
                        index < args.Count &&
                        args[index].Value is byte elementB)
                {
                    state = TranslateByte(elementB);
                    return true;
                }

                break;
            }
        }

        return false;
    }

    private void TryLoadGenericMetaTypeNullability(MemberInfo memberInfo, NullabilityInfo2 nullability)
    {
        MemberInfo? metaMember = GetMemberMetadataDefinition(memberInfo);
        Type? metaType = null;
        if (metaMember is FieldInfo field)
        {
            metaType = field.FieldType;
        }
        else if (metaMember is PropertyInfo property)
        {
            metaType = GetPropertyMetaType(property);
        }

        if (metaType != null)
        {
            CheckGenericParameters(nullability, metaMember!, metaType, memberInfo);
        }
    }

    private static MemberInfo GetMemberMetadataDefinition(MemberInfo member)
    {
        Type? type = member.DeclaringType;
        if ((type != null) && type.IsGenericType && !type.IsGenericTypeDefinition)
        {
            return type.GetGenericTypeDefinition().GetMemberWithSameMetadataDefinitionAs(member);
        }

        return member;
    }

    private static Type GetPropertyMetaType(PropertyInfo property)
    {
        if (property.GetGetMethod(true) is MethodInfo method)
        {
            return method.ReturnType;
        }

        return property.GetSetMethod(true)!.GetParameters()[0].ParameterType;
    }

    private void CheckGenericParameters(NullabilityInfo2 nullability, MemberInfo metaMember, Type metaType, MemberInfo originalMember)
    {
        if (metaType.IsGenericParameter)
        {
            NullabilityState state = nullability.ReadState;

            // issue: not respecting type constraints (class, struct, etc) in interpretation
            if (state == NullabilityState.NotNull && !ParseNullableState(metaType.GetCustomAttributesData(), 0, ref state))
            {
                state = GetNullableContext(metaType);
            }

            // issue: overrides rather than constrains
            nullability.ReadState = state;
            nullability.WriteState = state;
        }
        else if (metaType.ContainsGenericParameters)
        {
            if (nullability.GenericTypeArguments.Length > 0)
            {
                Type[] genericArguments = metaType.GetGenericArguments();

                for (int i = 0; i < genericArguments.Length; i++)
                {
                    if (genericArguments[i].IsGenericParameter)
                    {
                        NullabilityInfo2 n = GetNullabilityInfo(metaMember, genericArguments[i], genericArguments[i].GetCustomAttributesData(), i + 1);
                        nullability.GenericTypeArguments[i].ReadState = n.ReadState;
                        nullability.GenericTypeArguments[i].WriteState = n.WriteState;
                    }
                    else
                    {
                        UpdateGenericArrayElements(nullability.GenericTypeArguments[i].ElementType, metaMember, genericArguments[i]);
                    }
                }
            }
            else
            {
                UpdateGenericArrayElements(nullability.ElementType, metaMember, metaType);
            }
        }
    }

    private void UpdateGenericArrayElements(NullabilityInfo2? elementState, MemberInfo metaMember, Type metaType)
    {
        if (metaType.IsArray && elementState != null
            && metaType.GetElementType()!.IsGenericParameter)
        {
            Type elementType = metaType.GetElementType()!;
            NullabilityInfo2 n = GetNullabilityInfo(metaMember, elementType, elementType.GetCustomAttributesData(), 0);
            elementState.ReadState = n.ReadState;
            elementState.WriteState = n.WriteState;
        }
    }

    private static NullabilityState TranslateByte(object? value)
    {
        return value is byte b ? TranslateByte(b) : NullabilityState.Unknown;
    }

    private static NullabilityState TranslateByte(byte b) =>
        b switch
        {
            1 => NullabilityState.NotNull,
            2 => NullabilityState.Nullable,
            _ => NullabilityState.Unknown
        };
}

public sealed class NullabilityInfo2
{
    internal NullabilityInfo2(Type type, NullabilityState readState, NullabilityState writeState,
        NullabilityInfo2? elementType, NullabilityInfo2[] typeArguments)
    {
        Type = type;
        ReadState = readState;
        WriteState = writeState;
        ElementType = elementType;
        GenericTypeArguments = typeArguments;
    }

    /// <summary>
    /// The <see cref="System.Type" /> of the member or generic parameter
    /// to which this NullabilityInfo belongs
    /// </summary>
    public Type Type { get; }
    /// <summary>
    /// The nullability read state of the member
    /// </summary>
    public NullabilityState ReadState { get; internal set; }
    /// <summary>
    /// The nullability write state of the member
    /// </summary>
    public NullabilityState WriteState { get; internal set; }
    /// <summary>
    /// If the member type is an array, gives the <see cref="NullabilityInfo" /> of the elements of the array, null otherwise
    /// </summary>
    public NullabilityInfo2? ElementType { get; }
    /// <summary>
    /// If the member type is a generic type, gives the array of <see cref="NullabilityInfo" /> for each type parameter
    /// </summary>
    public NullabilityInfo2[] GenericTypeArguments { get; }
}

public enum NullabilityState2
{
    //
    // Summary:
    //     Nullability context not enabled (oblivious).
    Unknown,
    //
    // Summary:
    //     Non-nullable value or reference type.
    NotNull,
    //
    // Summary:
    //     Nullable value or reference type.
    Nullable,

    NotNullIfGenericArgumentIsNonNullableReferenceType,
    NullableIfGenericArgumentIsNonNullableReferenceType
}
