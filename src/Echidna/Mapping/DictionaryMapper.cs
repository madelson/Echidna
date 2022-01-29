using static System.Reflection.Emit.OpCodes;

namespace Medallion.Data.Mapping;

internal sealed class DictionaryMapper
{
    private readonly ScalarConverter _scalarConverter = new(); // todo

    public MappingResult Map(
        DictionaryTypeMappingStrategy strategy, 
        RowSchema schema, 
        string prefix, 
        Range range)
    {
        var bindings = new List<ColumnBinding>();
        var (offset, length) = range.GetOffsetAndLength(schema.ColumnCount);
        for (var i = 0; i < length; ++i)
        {
            var columnIndex = i + offset;
            if (schema.ColumnNames[columnIndex].StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                bindings.Add(new(
                    ColumnValueRetrieval.Create(
                        schema[columnIndex], 
                        strategy.ValueType, 
                        this._scalarConverter, 
                        isDestinationNonNullableReferenceType: strategy.IsValueTypeNonNullableReferenceType
                    ),
                    new DictionaryKeyBindingTarget(schema.ColumnNames[columnIndex], strategy.DictionaryType)
                ));
            }
        }

        return new(
            w =>
            {
                foreach (var (parameter, kind) in strategy.ConstructorParameters)
                {
                    switch (kind)
                    {
                        case DictionaryTypeMappingStrategy.ParameterKind.Capacity:
                            w.IL.Emit(Ldc_I4, bindings.Count); // stack is [..., capacity]
                            break;
                        case DictionaryTypeMappingStrategy.ParameterKind.Comparer:
                            w.IL.Emit(Call, MappingMethods.StringComparerOrdinalIgnoreCaseProperty.GetMethod!); // stack is [..., comparer]
                            break;
                        case DictionaryTypeMappingStrategy.ParameterKind.Defaulted:
                            w.EmitLoadConstant(parameter.ParameterType, parameter.DefaultValue);
                            break;
                        default:
                            throw Invariant.ShouldNeverGetHere();
                    }
                }
                w.IL.Emit(Newobj, strategy.Constructor); // stack is [dictionary]

                foreach (var binding in bindings)
                {
                    w.IL.Emit(Dup); // stack is [dictionary, dictionary]
                    w.IL.Emit(Ldstr, ((DictionaryKeyBindingTarget)binding.Target).Key); // stack is [dictionary, dictionary, key]
                    w.Emit(binding.Retrieval); // stack is [dictionary, dictionary, key, value]
                    w.IL.Emit(Call, strategy.AddMethod); // stack is [dictionary]
                }
            },
            bindings,
            IsPartialBinding: false
        );
    }
}
