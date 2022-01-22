using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Medallion.Data.Tests.Reflection;

// test cases:
// interface inheritance
// private member that is generic member
// private constructor (not method info)
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
    private readonly Dictionary<Module, NotAnnotatedStatus> _publicOnlyModulesCache = new();
    private readonly Dictionary<MemberInfo, NullabilityState> _contextCache = new();

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
        var stateParser = NullabilityStateParser.Create(this, parameterInfo.Member, parameterInfo);
        var metaParameter = GetMetaParameter(parameterInfo);
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
        var stateParser = NullabilityStateParser.Create(this, propertyInfo);
        var metaProperty = (PropertyInfo)GetMemberMetadataDefinition(propertyInfo);
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
        var stateParser = NullabilityStateParser.Create(this, eventInfo);
        var metaEvent = (EventInfo)GetMemberMetadataDefinition(eventInfo);
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
        var stateParser = NullabilityStateParser.Create(this, fieldInfo);
        var metaField = (FieldInfo)GetMemberMetadataDefinition(fieldInfo);
        PopulateNullabilityInfo(nullability, stateParser, metaField.FieldType, fieldInfo.ReflectedType);

        return nullability;
    }

    private void PopulateNullabilityInfo(
        NullabilityInfo nullability, 
        NullabilityStateParser stateParser,
        Type metaType,
        Type? reflectedContainerType)
    {
        stateParser.Parse(nullability.Type, metaType, out var readState, out var writeState);
        nullability.ReadState = readState;
        nullability.WriteState = writeState;

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
        if (!_publicOnlyModulesCache.TryGetValue(module, out NotAnnotatedStatus value))
        {
            value = PopulateAnnotationInfo(module.GetCustomAttributesData());
            _publicOnlyModulesCache.Add(module, value);
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

    internal static bool IsNullableValueType(Type type) =>
        type.IsGenericType
        && type.IsValueType
        && (type.IsGenericTypeDefinition ? type : type.GetGenericTypeDefinition()) == typeof(Nullable<>);

    private class NullabilityStateParser
    {
        private readonly object? _nullableAttributeArgument;
        private readonly MemberInfo? _contextLookupMember;
        private readonly NullabilityInfoContext _context;
        private readonly Type? _reflectedType;

        private NullabilityState _codeAnalysisAttributeReadState,
            _codeAnalysisAttributeWriteState;
        private int _stateIndex = 0;

        public NullabilityStateParser(
            object? nullableAttributeArgument,
            NullabilityState codeAnalysisAttributeReadState,
            NullabilityState codeAnalysisAttributeWriteState,
            MemberInfo? contextLookupMember,
            NullabilityInfoContext context,
            Type? reflectedType)
        {
            _nullableAttributeArgument = nullableAttributeArgument;
            _codeAnalysisAttributeReadState = codeAnalysisAttributeReadState;
            _codeAnalysisAttributeWriteState = codeAnalysisAttributeWriteState;
            _contextLookupMember = contextLookupMember;
            _context = context;
            _reflectedType = reflectedType;
        }

        public static NullabilityStateParser Create(
            NullabilityInfoContext context, 
            MemberInfo member,
            ParameterInfo? parameter = null,
            Type? reflectedType = null)
        {
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
                    TryGetNullableAttributeArgument(attribute, out nullableAttributeArgument))
                {
                    // nothing more to do
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

            // Context can appear on containing methods or containing types. For types whose
            // nullable annotations are stripped, we don't want to look up context at all.
            var contextLookupType = context.IsMemberImpactedByPublicOnlyAnnotations(member) ? null
                : parameter != null ? member
                : member is Type type ? (type.IsGenericMethodParameter ? type.DeclaringMethod : type)
                : member.DeclaringType;

            return new(
                nullableAttributeArgument,
                codeAnalysisReadState,
                codeAnalysisWriteState,
                contextLookupType,
                context,
                member.ReflectedType
            );
        }

        private static bool TryGetNullableAttributeArgument(CustomAttributeData attribute, out object? argument)
        {
            if (attribute.AttributeType.Name == "NullableAttribute" &&
                attribute.AttributeType.Namespace == CompilerServicesNameSpace &&
                attribute.ConstructorArguments.Count == 1)
            {
                argument = attribute.ConstructorArguments[0].Value;
                return true;
            }

            argument = null;
            return false;
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
                    readState = writeState = TranslateNullableAttributeArgument(_nullableAttributeArgument, index, _contextLookupMember);
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

            if (metaType.IsGenericParameter && !type.IsValueType)
            {
                var parameterNullability = GetGenericParameterNullability(metaType, reflectedType: _reflectedType);
                readState = Constrain(readState, parameterNullability);
                writeState = Constrain(writeState, parameterNullability);

                // If we're returning the type of a generic parameter, we have to use a "softer" interpretation of
                // its nullability unless it has the non-null "class" constraint.
                if (type.IsGenericMethodParameter 
                    && !(parameterNullability == NullabilityState.NotNull 
                        && type.GenericParameterAttributes.HasFlag(GenericParameterAttributes.ReferenceTypeConstraint)))
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
        }

        private NullabilityState TranslateNullableAttributeArgument(object? argument, int index, MemberInfo? contextLookupMember) =>
            argument switch
            {
                byte b => TranslateByte(b),
                ReadOnlyCollection<CustomAttributeTypedArgument> args
                    when index < args.Count && args[index].Value is byte elementB => TranslateByte(elementB),
                _ when contextLookupMember is not null => GetNullabilityContext(contextLookupMember),
                _ => NullabilityState.Unknown
            };

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

        private NullabilityState GetNullabilityContext(MemberInfo contextLookupMember)
        {
            if (_context._contextCache.TryGetValue(contextLookupMember, out var cached))
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
                        _context._contextCache.Add(contextLookupMember, contextState);
                        return contextState;
                    }
                }

                member = member.DeclaringType;
            }
            while (member != null);

            return NullabilityState.Unknown;
        }

        private NullabilityState GetGenericParameterNullability(Type genericParameter, Type? reflectedType)
        {
            Debug.Assert(genericParameter.IsGenericParameter && !genericParameter.IsValueType);

            var state = GetBaselineGenericParameterNullability(genericParameter);

            if (!genericParameter.IsGenericMethodParameter && reflectedType is not null)
            {
                ConstrainGenericParameterNullabilityWithInheritance(reflectedType, ref state, out _);
            }

            return state;

            NullabilityState GetBaselineGenericParameterNullability(Type genericParameter)
            {
                Debug.Assert(genericParameter.IsGenericParameter && !genericParameter.IsValueType);

                foreach (var attribute in genericParameter.GetCustomAttributesData())
                {
                    if (TryGetNullableAttributeArgument(attribute, out var argument))
                    {
                        return TranslateNullableAttributeArgument(argument, 0, genericParameter.DeclaringType);
                    }
                }

                return NullabilityState.Unknown;
            }

            NullabilityState GetBaseTypeGenericArgumentNullability(Type type, int genericArgumentPosition)
            {
                Debug.Assert(type.BaseType is not null && type.BaseType.IsConstructedGenericType);

                object? nullableAttributeArgument = null;
                foreach (var attribute in type.BaseType!.GetCustomAttributesData())
                {
                    if (TryGetNullableAttributeArgument(attribute, out nullableAttributeArgument))
                    {
                        break;
                    }
                }

                var nullabilityStateIndex = 1; // start at 1 since index 0 is the type itself
                var baseTypeGenericArguments = type.BaseType!.GetGenericArguments();
                for (var i = 0; i < genericArgumentPosition; i++)
                {
                    nullabilityStateIndex += CountNullabilityStates(baseTypeGenericArguments[i]);
                }

                return TranslateNullableAttributeArgument(nullableAttributeArgument, nullabilityStateIndex, type);

                static int CountNullabilityStates(Type type) =>
                    (IsValueTypeWithoutAnnotation(type, out _) ? 0 : 1)
                        + (type.IsGenericType ? type.GetGenericArguments().Sum(CountNullabilityStates) : 0);
            }

            void ConstrainGenericParameterNullabilityWithInheritance(Type type, ref NullabilityState state, out Type? nextConstrainingGenericParameter)
            {
                var baseType = type.BaseType;
                if (baseType is null)
                {
                    nextConstrainingGenericParameter = null;
                }
                else if (!baseType.IsGenericType)
                {
                    nextConstrainingGenericParameter = null;
                    ConstrainGenericParameterNullabilityWithInheritance(baseType, ref state, out _);
                }
                else if (baseType.GetGenericTypeDefinition() == genericParameter.DeclaringType)
                {
                    var genericArgument = type.GetGenericArguments()[genericParameter.GenericParameterPosition];
                    if (genericArgument.IsGenericParameter)
                    {
                        nextConstrainingGenericParameter = genericArgument;
                        state = GetBaselineGenericParameterNullability(genericArgument);
                    }
                    else
                    {
                        nextConstrainingGenericParameter = null;
                        state = GetBaseTypeGenericArgumentNullability(type, genericParameter.GenericParameterPosition);
                    }
                }
                else
                {
                    ConstrainGenericParameterNullabilityWithInheritance(baseType, ref state, out var constrainingGenericParameter);
                    if (constrainingGenericParameter is not null)
                    {
                        var genericArgument = type.GetGenericArguments()[genericParameter.GenericParameterPosition];
                        if (genericArgument.IsGenericParameter)
                        {
                            nextConstrainingGenericParameter = genericArgument;
                            state = GetBaselineGenericParameterNullability(genericArgument);
                        }
                        else
                        {
                            nextConstrainingGenericParameter = null;
                            var constrainingState = GetBaseTypeGenericArgumentNullability(type, constrainingGenericParameter.GenericParameterPosition);
                            state = Constrain(state, constrainingState);
                        }
                    }
                    else
                    {
                        nextConstrainingGenericParameter = null;
                    }
                }
            }
        }

        NullabilityState Constrain(NullabilityState @base, NullabilityState constraint) =>
            @base switch
            {
                NullabilityState.NotNull => @base,
                NullabilityState.Nullable => constraint == NullabilityState.NotNull ? constraint : @base,
                NullabilityState.Unknown => constraint,
                NullabilityState.NotNullIfGenericArgumentIsNonNullableReferenceType =>
                    constraint == NullabilityState.NotNull || constraint == NullabilityState.Nullable ? constraint : @base,
                NullabilityState.NullableIfGenericArgumentIsNonNullableReferenceType =>
                    constraint != NullabilityState.Unknown ? constraint : @base,
                _ => throw new ArgumentException(nameof(@base)),
            };
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
