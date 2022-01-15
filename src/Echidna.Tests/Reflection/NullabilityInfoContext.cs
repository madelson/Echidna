using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Medallion.Data.Tests.Reflection;

// test cases:
// interface inheritance
// private member that is generic member
// private constructor (not method base)
// generic method in generic type
// nullablepubliconly but has other system.diagnostic attrs
// MaybeNull + NotNull, other combinations
// annotated nullability + explicit nullability attributes: who wins?
// properties with explicit nullability
// indexer property with AllowNull
// writeonly property
// Nullable<> constructor/properties

// reference: https://github.com/dotnet/roslyn/blob/main/docs/features/nullable-metadata.md

using NullabilityInfo = Medallion.Data.Tests.Reflection.NullabilityInfo3;
using NullabilityState = Medallion.Data.Tests.Mapping.NullabilityState2;

internal class NullabilityInfoContext
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

    /// <summary>
    /// Populates <see cref="NullabilityInfo" /> for the given <see cref="ParameterInfo" />.
    /// If the nullablePublicOnly feature is set for an assembly, like it does in .NET SDK, the private and/or internal member's
    /// nullability attributes are omitted, in this case the API will return NullabilityState.Unknown state.
    /// </summary>
    /// <param name="parameterInfo">The parameter which nullability info gets populated</param>
    /// <exception cref="ArgumentNullException">If the parameterInfo parameter is null</exception>
    /// <returns><see cref="NullabilityInfo" /></returns>
    public NullabilityInfo Create(ParameterInfo parameterInfo)
    {
        if (parameterInfo is null)
        {
            throw new ArgumentNullException(nameof(parameterInfo));
        }

        EnsureIsSupported();

        var nullability = new NullabilityInfo(parameterInfo.ParameterType);
        var metaParameter = GetMetaParameter(parameterInfo);
        var stateParser = CreateStateParser(metaParameter.Member, metaParameter);
        PopulateNullabilityInfo(nullability, stateParser, metaParameter.ParameterType, parameterInfo.Member.ReflectedType);

        return nullability;
    }

    /// <summary>
    /// Populates <see cref="NullabilityInfo" /> for the given <see cref="PropertyInfo" />.
    /// If the nullablePublicOnly feature is set for an assembly, like it does in .NET SDK, the private and/or internal member's
    /// nullability attributes are omitted, in this case the API will return NullabilityState.Unknown state.
    /// </summary>
    /// <param name="propertyInfo">The parameter which nullability info gets populated</param>
    /// <exception cref="ArgumentNullException">If the propertyInfo parameter is null</exception>
    /// <returns><see cref="NullabilityInfo" /></returns>
    public NullabilityInfo Create(PropertyInfo propertyInfo)
    {
        if (propertyInfo is null)
        {
            throw new ArgumentNullException(nameof(propertyInfo));
        }

        EnsureIsSupported();

        var nullability = new NullabilityInfo(propertyInfo.PropertyType);
        var metaProperty = (PropertyInfo)GetMemberMetadataDefinition(propertyInfo);
        var stateParser = CreateStateParser(metaProperty);
        PopulateNullabilityInfo(nullability, stateParser, metaProperty.PropertyType, propertyInfo.ReflectedType);

        // use Unknown for invalid property states (TODO this is somewhat inconsistent with parameters, esp. return parameters)
        if (!propertyInfo.CanRead) { nullability.ReadState = NullabilityState.Unknown; }
        if (!propertyInfo.CanWrite) { nullability.WriteState = NullabilityState.Unknown; }

        return nullability;
    }

    /// <summary>
    /// Populates <see cref="NullabilityInfo" /> for the given <see cref="EventInfo" />.
    /// If the nullablePublicOnly feature is set for an assembly, like it does in .NET SDK, the private and/or internal member's
    /// nullability attributes are omitted, in this case the API will return NullabilityState.Unknown state.
    /// </summary>
    /// <param name="eventInfo">The parameter which nullability info gets populated</param>
    /// <exception cref="ArgumentNullException">If the eventInfo parameter is null</exception>
    /// <returns><see cref="NullabilityInfo" /></returns>
    public NullabilityInfo Create(EventInfo eventInfo)
    {
        if (eventInfo is null)
        {
            throw new ArgumentNullException(nameof(eventInfo));
        }

        EnsureIsSupported();

        var nullability = new NullabilityInfo(eventInfo.EventHandlerType!);
        var metaEvent = (EventInfo)GetMemberMetadataDefinition(eventInfo);
        var stateParser = CreateStateParser(metaEvent);
        PopulateNullabilityInfo(nullability, stateParser, metaEvent.EventHandlerType!, eventInfo.ReflectedType);

        return nullability;
    }

    /// <summary>
    /// Populates <see cref="NullabilityInfo" /> for the given <see cref="FieldInfo" />
    /// If the nullablePublicOnly feature is set for an assembly, like it does in .NET SDK, the private and/or internal member's
    /// nullability attributes are omitted, in this case the API will return NullabilityState.Unknown state.
    /// </summary>
    /// <param name="fieldInfo">The parameter which nullability info gets populated</param>
    /// <exception cref="ArgumentNullException">If the fieldInfo parameter is null</exception>
    /// <returns><see cref="NullabilityInfo" /></returns>
    public NullabilityInfo Create(FieldInfo fieldInfo)
    {
        if (fieldInfo is null)
        {
            throw new ArgumentNullException(nameof(fieldInfo));
        }

        EnsureIsSupported();

        var nullability = new NullabilityInfo(fieldInfo.FieldType);
        var metaField = (FieldInfo)GetMemberMetadataDefinition(fieldInfo);
        var stateParser = CreateStateParser(metaField);
        PopulateNullabilityInfo(nullability, stateParser, metaField.FieldType, fieldInfo.ReflectedType);

        return nullability;
    }

    private static void PopulateNullabilityInfo(
        NullabilityInfo nullability, 
        NullabilityStateParser stateParser,
        Type metaType,
        Type? reflectedContainerType)
    {
        stateParser.Parse(nullability.Type, metaType, out var readState, out var writeState);

        if (metaType.IsGenericParameter && !metaType.IsValueType)
        {
            throw new NotImplementedException();
        }
        else
        {
            nullability.ReadState = readState;
            nullability.WriteState = writeState;
        }

        if (nullability.ElementType != null)
        {
            var elementMetaType = metaType.IsArray ? metaType.GetElementType()! : nullability.ElementType.Type;
            PopulateNullabilityInfo(nullability.ElementType, stateParser, elementMetaType, reflectedContainerType);
        }
        else if (nullability.GenericTypeArguments.Length != 0)
        {
            var metaTypeGenericArguments = metaType.IsGenericType ? metaType.GetGenericArguments() : null;
            for (var i = 0; i < nullability.GenericTypeArguments.Length; i++)
            {
                var genericArgumentNullability = nullability.GenericTypeArguments[i];
                var genericArgumentMetaType = metaTypeGenericArguments?[i] ?? genericArgumentNullability.Type;
                PopulateNullabilityInfo(genericArgumentNullability, stateParser, genericArgumentMetaType, reflectedContainerType);
            }
        }
    }

    private static ParameterInfo GetMetaParameter(ParameterInfo parameter)
    {
        if (parameter.Member is not MethodInfo method)
        {
            return parameter;
        }

        var metaMethod = GetMethodMetadataDefinition(method);
        if (metaMethod == method)
        {
            return parameter;
        }

        if (parameter.Position == -1)
        {
            return metaMethod.ReturnParameter;
        }

        var metaParameters = metaMethod.GetParameters();
        for (int i = 0; i < metaParameters.Length; i++)
        {
            if (parameter.Position == i &&
                parameter.Name == metaParameters[i].Name)
            {
                return metaParameters[i];
            }
        }

        return parameter;
    }

    private static MethodInfo GetMethodMetadataDefinition(MethodInfo method)
    {
        if (method.IsGenericMethod && !method.IsGenericMethodDefinition)
        {
            method = method.GetGenericMethodDefinition();
        }

        return (MethodInfo)GetMemberMetadataDefinition(method);
    }

    private static MemberInfo GetMemberMetadataDefinition(MemberInfo member)
    {
        Type? type = member.DeclaringType;
        if (type != null && type.IsGenericType && !type.IsGenericTypeDefinition)
        {
            return type.GetGenericTypeDefinition().GetMemberWithSameMetadataDefinitionAs(member);
        }

        return member;
    }

    private bool IsMemberImpactedByPublicOnlyAnnotations(MemberInfo? member)
    {
        var (isPrivate, isFamilyAndAssembly, isAssembly) = GetVisibility(member);

        return isPrivate || isFamilyAndAssembly || isAssembly
            ? IsImpactedByPublicOnlyAnnotations(isPrivate, isFamilyAndAssembly, isAssembly, member!.Module)
            : false;

        static (bool IsPrivate, bool IsFamilyAndAssembly, bool IsAssembly) GetVisibility(MemberInfo? member) => member switch
        {
            FieldInfo field => (field.IsPrivate, field.IsFamilyAndAssembly, field.IsAssembly), 
            MethodBase method => (method.IsPrivate, method.IsFamilyAndAssembly, method.IsAssembly),
            // From https://github.com/dotnet/roslyn/blob/main/docs/features/nullable-metadata.md:
            // "For members that do not have explicit accessibility in metadata (specifically for parameters,
            // type parameters, events, and properties), the compiler uses the accessibility of the
            // container to determine whether to emit nullable attributes."
            PropertyInfo property => GetVisibility(property.DeclaringType),
            EventInfo @event => GetVisibility(@event.DeclaringType),
            _ => (false, false, false),
        };
    }

    private bool IsImpactedByPublicOnlyAnnotations(bool isPrivate, bool isFamilyAndAssembly, bool isAssembly, Module module)
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

    private static NotAnnotatedStatus PopulateAnnotationInfo(IList<CustomAttributeData> customAttributes)
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

    private static void EnsureIsSupported()
    {
        if (!IsSupported)
        {
            throw new InvalidOperationException("not supported");
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

    private NullabilityStateParser CreateStateParser(MemberInfo member, ParameterInfo? parameter = null)
    {
        Debug.Assert(member is not Type);

        object? nullableAttributeArgument = null;
        NullabilityState codeAnalysisReadState = NullabilityState.Unknown,
            codeAnalysisWriteState = NullabilityState.Unknown;

        IEnumerable<CustomAttributeData> attributes;
        if (parameter != null)
        {
            attributes = parameter.GetCustomAttributesData();
        }
        else
        {
            attributes = member.GetCustomAttributesData();
            // For properties, attributes like AllowNull are found on the return/value parameters of the getters/setters
            // instead of on the property itself.
            if (member is PropertyInfo property)
            {
                if (property.CanRead)
                {
                    attributes = attributes.Concat(property.GetMethod!.ReturnParameter.GetCustomAttributesData());
                }
                if (property.CanWrite)
                {
                    var valueParameter = property.SetMethod!.GetParameters()[^1];
                    attributes = attributes.Concat(valueParameter.GetCustomAttributesData());
                }
            }
        }
        foreach (var attribute in attributes)
        {
            if (nullableAttributeArgument is null &&
                attribute.AttributeType.Name == "NullableAttribute" &&
                attribute.AttributeType.Namespace == CompilerServicesNameSpace &&
                attribute.ConstructorArguments.Count == 1)
            {
                nullableAttributeArgument = attribute.ConstructorArguments[0].Value;
            }
            else if (attribute.AttributeType.Namespace == "System.Diagnostics.CodeAnalysis")
            {
                switch (attribute.AttributeType.Name)
                {
                    case "NotNullAttribute":
                        codeAnalysisReadState = NullabilityState.NotNull;
                        break;
                    case "MaybeNullAttribute":
                    case "MaybeNullWhenAttribute":
                        // note: if both MaybeNull and NotNull are present, NotNull wins
                        if (codeAnalysisReadState == NullabilityState.Unknown)
                        {
                            codeAnalysisReadState = NullabilityState.Nullable;
                        }
                        break;
                    case "DisallowNullAttribute":
                        codeAnalysisWriteState = NullabilityState.NotNull;
                        break;
                    case "AllowNullAttribute":
                        if (codeAnalysisWriteState == NullabilityState.Unknown)
                        {
                            codeAnalysisWriteState = NullabilityState.Nullable;
                        }
                        break;
                }
            }

            Debug.Assert(
                attribute.AttributeType.Name != "NullableContextAttribute" ||
                attribute.AttributeType.Namespace != CompilerServicesNameSpace);
        }

        var isImpactedByPublicOnly = IsMemberImpactedByPublicOnlyAnnotations(member);
        Debug.Assert(!isImpactedByPublicOnly || nullableAttributeArgument == null);

        return new(
            nullableAttributeArgument,
            codeAnalysisReadState,
            codeAnalysisWriteState,
            // If our annotations were stripped, we don't want to look up context
            // since that won't be accurate.
            // For a parameter we start looking for context on the member (method). Otherwise,
            // member can't have context (which is only on types and methods), so look in the container
            isImpactedByPublicOnly ? null : parameter != null ? member : member.DeclaringType,
            _context
        );
    }

    private class NullabilityStateParser
    {
        private readonly object? _nullableAttributeArgument;
        private readonly MemberInfo? _contextLookupMember;
        private readonly Dictionary<MemberInfo, NullabilityState> _contextCache;

        private NullabilityState _codeAnalysisAttributeReadState,
            _codeAnalysisAttributeWriteState;
        private int _stateIndex = 0;

        public NullabilityStateParser(
            object? nullableAttributeArgument,
            NullabilityState codeAnalysisAttributeReadState,
            NullabilityState codeAnalysisAttributeWriteState,
            MemberInfo? contextLookupMember,
            Dictionary<MemberInfo, NullabilityState> contextCache)
        {
            _nullableAttributeArgument = nullableAttributeArgument;
            _codeAnalysisAttributeReadState = codeAnalysisAttributeReadState;
            _codeAnalysisAttributeWriteState = codeAnalysisAttributeWriteState;
            _contextLookupMember = contextLookupMember;
            _contextCache = contextCache;
        }

        public void Parse(Type type, Type metaType, out NullabilityState readState, out NullabilityState writeState)
        {
            var index = _stateIndex;
            bool respectCodeAnalysisAnnotations;

            if (IsValueTypeWithoutAnnotation(metaType, out var isMetaTypeNullableValueType))
            {
                readState = writeState = isMetaTypeNullableValueType ? NullabilityState.Nullable : NullabilityState.NotNull;
                respectCodeAnalysisAnnotations = isMetaTypeNullableValueType;
            }
            else
            {
                _stateIndex = index + 1;

                if (type.IsValueType)
                {
                    var isTypeNullableValueType = isMetaTypeNullableValueType || IsNullableValueType(type);
                    readState = writeState = isTypeNullableValueType ? NullabilityState.Nullable : NullabilityState.NotNull;
                    respectCodeAnalysisAnnotations = isTypeNullableValueType;
                }
                else
                {
                    var nullableAttributeState = _nullableAttributeArgument switch
                    {
                        byte b => TranslateByte(b),
                        ReadOnlyCollection<CustomAttributeTypedArgument> args
                            when index < args.Count && args[index].Value is byte elementB => TranslateByte(elementB),
                        _ => GetNullabilityContext(),
                    };

                    readState = writeState = nullableAttributeState;
                    respectCodeAnalysisAnnotations = true;
                }
            }

            if (index == 0)
            {
                if (respectCodeAnalysisAnnotations)
                {
                    if (_codeAnalysisAttributeReadState != NullabilityState.Unknown)
                    {
                        readState = _codeAnalysisAttributeReadState;
                    }
                    if (_codeAnalysisAttributeWriteState != NullabilityState.Unknown)
                    {
                        writeState = _codeAnalysisAttributeWriteState;
                    }
                }
                _codeAnalysisAttributeReadState = _codeAnalysisAttributeWriteState = NullabilityState.Unknown;
            }

            if (metaType.IsGenericParameter
                && !type.IsValueType
                && !metaType.GenericParameterAttributes.HasFlag(GenericParameterAttributes.ReferenceTypeConstraint))
            {
                readState = InterpretNullabilityForUnconstrainedGenericParameter(readState);
                writeState = InterpretNullabilityForUnconstrainedGenericParameter(writeState);

                static NullabilityState InterpretNullabilityForUnconstrainedGenericParameter(NullabilityState state) => state switch
                {
                    NullabilityState.Nullable => NullabilityState.NullableIfGenericArgumentIsNonNullableReferenceType,
                    NullabilityState.NotNull => NullabilityState.NotNullIfGenericArgumentIsNonNullableReferenceType,
                    _ => state
                };
            }
        }

        private static bool IsValueTypeWithoutAnnotation(Type type, out bool isNullableValueType)
        {
            isNullableValueType = IsNullableValueType(type);
            if (isNullableValueType) { return true; }
            if (!type.IsValueType) { return false; }
            if (!type.IsGenericType) { return true; }

            var genericArguments = type.GetGenericArguments();
            foreach (var genericArgument in genericArguments)
            {
                if (!IsValueTypeWithoutAnnotation(genericArgument, out _))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsNullableValueType(Type type) =>
            Nullable.GetUnderlyingType(type) is not null || type == typeof(Nullable<>);

        private NullabilityState GetNullabilityContext()
        {
            var contextLookupMember = _contextLookupMember;
            if (contextLookupMember is not null)
            {
                if (_contextCache.TryGetValue(contextLookupMember, out var cached))
                {
                    return cached;
                }

                MemberInfo? member = contextLookupMember;
                do
                {
                    foreach (var attribute in member!.GetCustomAttributesData())
                    {
                        if (attribute.AttributeType.Name == "NullableContextAttribute" &&
                            attribute.AttributeType.Namespace == CompilerServicesNameSpace &&
                            attribute.ConstructorArguments.Count == 1)
                        {
                            var contextState = TranslateByte(attribute.ConstructorArguments[0].Value);
                            _contextCache.Add(contextLookupMember, contextState);
                            return contextState;
                        }
                    }

                    member = member.DeclaringType;
                }
                while (member != null);
            }

            return NullabilityState.Unknown;
        }
    }
}

public sealed class NullabilityInfo3
{
    internal NullabilityInfo3(Type type)
    {
        Type = type;
        if (type.IsGenericType)
        {
            var genericArguments = type.GetGenericArguments();
            GenericTypeArguments = new NullabilityInfo[genericArguments.Length];
            for (var i = 0; i < genericArguments.Length; i++)
            {
                GenericTypeArguments[i] = new(genericArguments[i]);
            }
        }
        else
        {
            GenericTypeArguments = Array.Empty<NullabilityInfo>();

            if (type.IsArray)
            {
                ElementType = new(type.GetElementType()!);
            }
        }
    }

    internal NullabilityInfo3(Type type, NullabilityState readState, NullabilityState writeState,
        NullabilityInfo? elementType, NullabilityInfo[] typeArguments)
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
    public NullabilityInfo? ElementType { get; }
    /// <summary>
    /// If the member type is a generic type, gives the array of <see cref="NullabilityInfo" /> for each type parameter
    /// </summary>
    public NullabilityInfo[] GenericTypeArguments { get; }
}
