using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using static System.Reflection.Emit.OpCodes;

namespace Medallion.Data.Mapping;

internal sealed class DictionaryMapper : ITypeMapper
{
    private readonly ScalarConverter _scalarConverter = new(); // todo

    public bool TryBind(RowSchema schema, Type destination, [NotNullWhen(returnValue: true)] out List<ColumnBinding>? bindings)
    {
        if (!IsMappableDictionaryType(destination, out var valueType))
        {
            bindings = null;
            return false;
        }

        bindings = new List<ColumnBinding>(capacity: schema.ColumnCount);
        for (var i = 0; i < schema.ColumnCount; ++i)
        {
            bindings.Add(new(
                ColumnValueRetrieval.Create(schema[i], valueType, this._scalarConverter),
                new DictionaryKeyBindingTarget(schema.ColumnNames[i], destination)
            ));
        }
        return true;
    }

    public bool TryCreateMapperWriter(
        IReadOnlyCollection<ColumnBinding> bindings, 
        [NotNullWhen(returnValue: true)] out RowMapperWriter? writer)
    {
        Invariant.Require(bindings.Any());
        Invariant.Require(bindings.All(b => b.Target is DictionaryKeyBindingTarget) || !bindings.Any(b => b.Target is DictionaryKeyBindingTarget));

        var firstBinding = bindings.First();
        if (firstBinding.Target is not DictionaryKeyBindingTarget firstTarget)
        {
            writer = null;
            return false;
        }

        Invariant.Require(bindings.All(b => ((DictionaryKeyBindingTarget)b.Target).DictionaryType == firstTarget.DictionaryType));

        var isExpando = !firstTarget.DictionaryType.IsConstructedGenericType;
        var dictionaryType = isExpando || !firstTarget.DictionaryType.IsInterface
            ? firstTarget.DictionaryType
            : typeof(Dictionary<,>).MakeGenericType(firstTarget.DictionaryType.GetGenericArguments());

        ConstructorInfo constructor;
        MethodInfo addMethod;
        if (isExpando)
        {
            constructor = dictionaryType.GetConstructor(Type.EmptyTypes)
                ?? throw Invariant.ShouldNeverGetHere();
            addMethod = dictionaryType.GetMethod("System.Collections.Generic.IDictionary<System.String,System.Object>.Add", BindingFlags.NonPublic | BindingFlags.Instance, new[] { typeof(string), typeof(object) })
                ?? throw Invariant.ShouldNeverGetHere();
        }
        else
        {
            constructor = dictionaryType.GetConstructor(new[] { typeof(int), typeof(IEqualityComparer<string>) })
                ?? throw Invariant.ShouldNeverGetHere();
            addMethod = dictionaryType.GetMethod("Add", BindingFlags.Public | BindingFlags.Instance, new[] { typeof(string), firstBinding.Retrieval.DestinationType })
                ?? throw Invariant.ShouldNeverGetHere();
        }

        writer = new(
            w =>
            {
                if (!isExpando)
                {
                    w.IL.Emit(Ldc_I4, bindings.Count); // stack is [capacity]
                    w.IL.Emit(Call, MappingMethods.StringComparerOrdinalIgnoreCaseProperty.GetMethod!); // stack is [capacity, comparer]
                }
                w.IL.Emit(Newobj, constructor); // stack is [dictionary]
                
                foreach (var binding in bindings)
                {
                    w.IL.Emit(Dup); // stack is [dictionary, dictionary]
                    w.IL.Emit(Ldstr, ((DictionaryKeyBindingTarget)binding.Target).Key); // stack is [dictionary, dictionary, key]
                    w.Emit(binding.Retrieval); // stack is [dictionary, dictionary, key, value]
                    w.IL.Emit(Call, addMethod); // stack is [dictionary]
                }
            },
            Bindings: bindings
        );
        return true;
    }

    private static bool IsMappableDictionaryType(Type destination, [NotNullWhen(returnValue: true)] out Type? valueType)
    {
        var destinationName = destination.Name;
        var destinationNamespace = destination.Namespace;

        // TODO probably remove this because of casing behavior. EO is case-sensitive by default but
        // that breaks because OracleProvider reads as UPPERCASE and Postgres reads as LOWERCASE
        // See https://stackoverflow.com/questions/7760035/how-to-set-expandoobjects-dictionary-as-case-insensitive
        // For an alternative
        if (destinationName == "ExpandoObject" && destinationNamespace == "System.Dynamic")
        {
            valueType = typeof(object);
            return true;
        }

        if (destinationNamespace == "System.Collections.Generic" && destination.IsConstructedGenericType)
        {
            var genericTypeDefinition = destination.GetGenericTypeDefinition();
            if (genericTypeDefinition == typeof(Dictionary<,>)
                || genericTypeDefinition == typeof(IDictionary<,>)
                || genericTypeDefinition == typeof(IReadOnlyDictionary<,>))
            {
                var genericArguments = destination.GetGenericArguments();
                if (genericArguments[0] == typeof(string))
                {
                    valueType = genericArguments[1];
                    return true;
                }
            }
        }

        valueType = null;
        return false;
    }
}
