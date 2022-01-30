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
        var columns = schema.GetColumns(prefix, range);
        var bindings = columns.Select(t => new ColumnBinding(
                ColumnValueRetrieval.Create(t.Column, strategy.ValueType, this._scalarConverter, strategy.IsValueTypeNonNullableReferenceType),
                new DictionaryKeyBindingTarget(t.Name, strategy.DictionaryType)
            ))
            .ToArray();

        return new(
            (w, loader) =>
            {
                foreach (var (parameter, kind) in strategy.ConstructorParameters)
                {
                    switch (kind)
                    {
                        case DictionaryTypeMappingStrategy.ParameterKind.Capacity:
                            w.IL.Emit(Ldc_I4, bindings.Length); // stack is [..., capacity]
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
                    loader.EmitLoad(binding.Retrieval); // stack is [dictionary, dictionary, key, value]
                    w.IL.Emit(Call, strategy.AddMethod); // stack is [dictionary]
                }
            },
            bindings,
            IsPartialBinding: false
        );
    }
}
