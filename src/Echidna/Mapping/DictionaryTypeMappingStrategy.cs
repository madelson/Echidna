using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Medallion.Data.Mapping;

internal sealed class DictionaryTypeMappingStrategy : CompositeTypeMappingStrategy
{
    public Type ValueType { get; }
    
    public bool IsValueTypeNonNullableReferenceType { get; }

    public ConstructorInfo Constructor { get; }

    public IReadOnlyDictionary<ParameterInfo, ParameterKind> ConstructorParameters { get; }

    public MethodInfo AddMethod { get; }

    private DictionaryTypeMappingStrategy(
        Type implementedIDictionaryInterface,
        Type valueType,
        NullabilityInfo? nullabilityInfo,
        ConstructorInfo constructor,
        IReadOnlyDictionary<ParameterInfo, ParameterKind> constructorParameters)
    {
        var addMethod = GetAddMethod();
        this.ValueType = valueType;
        this.IsValueTypeNonNullableReferenceType = IsValueTypeNonNullableReferenceType();
        this.Constructor = constructor;
        this.ConstructorParameters = constructorParameters;
        this.AddMethod = addMethod;

        bool IsValueTypeNonNullableReferenceType()
        {
            if (valueType.IsValueType) { return false; }

            var dictionaryType = constructor.DeclaringType!;

            // If we're provided with nullability info, we might be able to use its generic type arguments.
            // However, we can only do so if this type is a generic type whose arguments line up with the
            // dictionary's TValue
            if (nullabilityInfo != null && dictionaryType.IsConstructedGenericType)
            {
                var genericArguments = dictionaryType.GetGenericArguments();
                if (genericArguments.Contains(valueType))
                {
                    var genericTypeDefinition = dictionaryType.GetGenericTypeDefinition();
                    var typeDefinitionGenericArguments = genericTypeDefinition.GetGenericArguments();
                    var genericTypeDefinitionDictionaryInterfaces = genericTypeDefinition.GetInterfaces()
                        .Where(i => i.IsConstructedGenericType && i.GetGenericTypeDefinition() == typeof(IDictionary<,>));
                    foreach (var genericTypeDefinitionDictionaryInterface in genericTypeDefinitionDictionaryInterfaces)
                    {
                        var interfaceGenericArguments = genericTypeDefinitionDictionaryInterface.GetGenericArguments();

                        // make sure the interface's key is string or a TKey which is string in the dictionary type
                        var interfaceKeyType = interfaceGenericArguments[0];
                        if (interfaceKeyType != typeof(string)
                            && !Enumerable.Range(0, genericArguments.Length)
                                .Any(i => interfaceKeyType == typeDefinitionGenericArguments[i] && genericArguments[i] == typeof(string)))
                        {
                            continue;
                        }

                        // see if the interface's value type is a TValue which is one of the dictionary type's generic arguments
                        
                        var interfaceValueType = interfaceGenericArguments[1];
                        if (interfaceValueType == valueType) { break; } // value type not bound to a generic parameter; we'll have to look elsewhere
                        
                        var index = Array.IndexOf(typeDefinitionGenericArguments, interfaceValueType);
                        if (index >= 0)
                        {
                            return nullabilityInfo.GenericTypeArguments[index].WriteState == NullabilityState.NotNull;
                        }
                    }
                }
            }

            // This approach doesn't work, currently. See https://github.com/dotnet/runtime/issues/63555 
            //// If dictionary type doesn't implement IDictionary<string, TValue> using its own generics,
            //// we should be able to grab nullability from the add method's parameters
            //if (Nullability.TryGetFor(addMethod.GetParameters()[1], out var addMethodNullability))
            //{
            //    return addMethodNullability.WriteState == NullabilityState.NotNull;
            //}

            return false;
        }

        MethodInfo GetAddMethod()
        {
            var interfaceMap = constructor.DeclaringType!.GetInterfaceMap(implementedIDictionaryInterface);
            var addIndex = Array.FindIndex(
                interfaceMap.InterfaceMethods,
                m => m.Name == nameof(IDictionary<string, object>.Add)
                    && m.ReturnType == typeof(void)
                    && m.GetParameters().Select(p => p.ParameterType).SequenceEqual(new[] { typeof(string), valueType })
            );
            return interfaceMap.TargetMethods[addIndex];
        }
    }

    public static bool TryCreateDictionaryStrategyFor(
        Type type,
        NullabilityInfo? nullabilityInfo,
        [NotNullWhen(returnValue: true)] out CompositeTypeMappingStrategy? strategy,
        [NotNullWhen(returnValue: false)] out string? errorMessage)
    {
        if (type.IsInterface)
        {
            const string InterfaceErrorMessage = 
                "Only interfaces IDictionary<string, V>, IReadOnlyDictionary<string, V>, and IDictionary can be mapped to dictionaries";

            Type[] genericArguments;
            if (type.IsConstructedGenericType)
            {
                var genericTypeDefinition = type.GetGenericTypeDefinition();
                if (genericTypeDefinition != typeof(IDictionary<,>)
                    && genericTypeDefinition != typeof(IReadOnlyDictionary<,>))
                {
                    return Error(InterfaceErrorMessage, out strategy, out errorMessage);
                }

                genericArguments = type.GetGenericArguments();
                if (genericArguments[0] != typeof(string))
                {
                    return Error(InterfaceErrorMessage, out strategy, out errorMessage);
                }
            }
            else if (type != typeof(IDictionary))
            {
                genericArguments = new[] { typeof(string), typeof(object) };
                return Error(InterfaceErrorMessage, out strategy, out errorMessage);
            }

            // interfaces fall back to Dictionary<K, V>
            // TODO IROD should fall back to Row if we build that
            return TryCreateFor(typeof(Dictionary<string, object>), nullabilityInfo, out strategy, out errorMessage);
        }

        if (type.IsAbstract)
        {
            return Error("An abstract type cannot be mapped to a dictionary", out strategy, out errorMessage);
        }

        var interfaceAndGenericArgumentsCandidates = type.GetInterfaces()
            .Where(i => i.IsConstructedGenericType && i.GetGenericTypeDefinition() == typeof(IDictionary<,>))
            .Select(i => (Interface: i, GenericArguments: i.GetGenericArguments()))
            .Where(i => i.GenericArguments[0] == typeof(string))
            .ToArray();
        if (interfaceAndGenericArgumentsCandidates.Length == 0)
        {
            return Error("Concrete types must implement IDictionary<string, V> to be mapped to a dictionary", out strategy, out errorMessage);
        }
        if (interfaceAndGenericArgumentsCandidates.Length > 1)
        {
            return Error("Cannot be mapped to a dictionary because the type implements IDictionary<string, V> for multiple types V", out strategy, out errorMessage);
        }
        var interfaceAndGenericArguments = interfaceAndGenericArgumentsCandidates[0];

        var constructor = type.GetConstructors(BindingFlags.Public | BindingFlags.Instance)
            .Select(c => (Constructor: c, Parameters: c.GetParameters().ToDictionary(p => p, MapParameter)))
            .Where(
                c => !c.Parameters.Values.Contains(ParameterKind.Invalid) // none invalid
                    && c.Parameters.Values.Count(k => k == ParameterKind.Capacity) <= 1 // at most one capacity
                    && c.Parameters.Values.Count(k => k == ParameterKind.Comparer) <= 1 // at most one comparer
            )
            // prefer being able to specify a comparer
            .OrderByDescending(c => c.Parameters.Values.Contains(ParameterKind.Comparer))
            // then prefer being able to specify capacity
            .ThenByDescending(c => c.Parameters.Values.Contains(ParameterKind.Capacity))
            // then prefer the fewest parameters
            .ThenBy(c => c.Parameters.Count)
            // then arbitrarily but consistently to break any ties
            .ThenBy(c => c.ToString())
            .FirstOrDefault();
        if (constructor.Constructor == null)
        {
            return Error(
                "To be mapped to a dictionary, a type must have a public constructor where each parameter is either (a) a capacity parameter, (b) a comparer parameter, or (c) a parameter with a default value", 
                out strategy, 
                out errorMessage
            );
        }

        return Success(
            new DictionaryTypeMappingStrategy(
                implementedIDictionaryInterface: interfaceAndGenericArguments.Interface,
                valueType: interfaceAndGenericArguments.GenericArguments[1],
                nullabilityInfo: nullabilityInfo,
                constructor: constructor.Constructor,
                constructorParameters: constructor.Parameters
            ),
            out strategy,
            out errorMessage
        );
    }

    private static ParameterKind MapParameter(ParameterInfo parameter) =>
        parameter.ParameterType == typeof(int) && parameter.Name == "capacity" ? ParameterKind.Capacity
            : parameter.ParameterType.IsAssignableFrom(typeof(StringComparer)) ? ParameterKind.Comparer
            : parameter.HasDefaultValue ? ParameterKind.Defaulted
            : ParameterKind.Invalid;

    public enum ParameterKind
    {
        Capacity,
        Comparer,
        Defaulted,
        Invalid,
    }
}
