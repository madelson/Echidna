using System.Reflection;
using static System.Reflection.Emit.OpCodes;

namespace Medallion.Data.Mapping;

internal class PocoMapper
{
    private readonly ScalarConverter _scalarConverter = new(); // todo

    public MappingResult? TryMap(
        PocoTypeMappingStrategy strategy,
        RowSchema schema,
        string prefix,
        Range range)
    {
        var columns = schema.GetColumns(prefix, range);
        var (constructor, bindings) = this.FindConstructor(strategy, columns);
        var isValueTypePoco = strategy.PocoType.IsValueType;
        if (constructor is null && !isValueTypePoco) { return null; }

        bindings ??= new Dictionary<string, ColumnBinding>(StringComparer.OrdinalIgnoreCase);
        this.BindPropertiesAndFields(columns, strategy, bindings);
        if (bindings.Count == 0) { return null; }

        return new(
            (w, loader) =>
            {
                var usePocoVariable = isValueTypePoco 
                    && (constructor is null || bindings.Values.Any(b => b.Target is not ConstructorParameterBindingTarget)); 
                using var pocoVariable = usePocoVariable ? w.UseLocal(strategy.PocoType) : default;

                if (usePocoVariable)
                {
                    w.IL.Emit(Ldloca, pocoVariable!);
                }

                if (constructor != null)
                {
                    foreach (var parameter in constructor.GetParameters())
                    {
                        if (parameter.Name != null && bindings.TryGetValue(parameter.Name, out var binding))
                        {
                            loader.EmitLoad(binding.Retrieval);
                        }
                        else
                        {
                            Invariant.Require(parameter.HasDefaultValue);
                            w.EmitLoadConstant(parameter.ParameterType, parameter.DefaultValue);
                        }
                    }

                    w.IL.Emit(Newobj, constructor);
                }
                else
                {
                    Invariant.Require(usePocoVariable);
                    w.IL.Emit(Initobj, strategy.PocoType);
                }

                // note: going in index order minimizes the amount of locals used
                foreach (var binding in bindings.Values.OrderBy(b => b.Retrieval.Column.Index))
                {
                    var member = binding.Target switch
                    {
                        PropertyBindingTarget property => property.Property,
                        FieldBindingTarget field => field.Field,
                        ConstructorParameterBindingTarget _ => default(MemberInfo),
                        _ => throw Invariant.ShouldNeverGetHere()
                    };
                    if (member != null)
                    {
                        if (usePocoVariable)
                        {
                            w.IL.Emit(Ldloca, pocoVariable!); // stack is [&poco]
                        }
                        else
                        {
                            w.IL.Emit(Dup); // stack is [poco, poco]
                        }

                        loader.EmitLoad(binding.Retrieval); // stack is [..., poco|&poco, value]
                        if (member is PropertyInfo property)
                        {
                            w.IL.Emit(Call, property.SetMethod!); // stack is [...]
                        }
                        else if (member is FieldInfo field)
                        {
                            w.IL.Emit(Stfld, field); // stack is [...]
                        }
                    }
                }

                if (usePocoVariable)
                {
                    w.IL.Emit(Ldloc, pocoVariable!); // stack is [poco]
                }
            },
            bindings.Values.OrderBy(b => b.Retrieval.Column.Index).ToArray(),
            IsPartialBinding: bindings.Count < strategy.NameMapping.Count
        );
    }

    private (ConstructorInfo? Constructor, Dictionary<string, ColumnBinding>? Bindings) FindConstructor(
        PocoTypeMappingStrategy strategy, 
        IReadOnlyList<(string Name, Column Column)> columns)
    {
        var columnsByName = columns.ToDictionary(c => c.Name, c => c.Column, StringComparer.OrdinalIgnoreCase);

        ConstructorInfo? bestConstructor = null;
        Dictionary<string, ColumnBinding>? bestConstructorBindings = null;
        foreach (var constructor in strategy.Constructors)
        {
            var bindings = this.BindConstructor(constructor, strategy, columnsByName);
            if (bindings != null
                && (
                    bestConstructorBindings is null
                    || bestConstructorBindings.Count < bindings.Count
                    // TODO could prefer constructor with safer bindings
                    || (bestConstructorBindings.Count == bindings.Count && constructor.GetParameters().Length < bestConstructor!.GetParameters().Length)
                ))
            {
                bestConstructor = constructor;
                bestConstructorBindings = bindings;
            }
        }

        return (bestConstructor, bestConstructorBindings);
    }

    private Dictionary<string, ColumnBinding>? BindConstructor(
        ConstructorInfo constructor, 
        PocoTypeMappingStrategy strategy,
        IReadOnlyDictionary<string, Column> columns)
    {
        var result = new Dictionary<string, ColumnBinding>(capacity: columns.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var parameter in constructor.GetParameters())
        {
            if (parameter.ParameterType.IsByRef) { return null; }

            if (parameter.Name != null 
                && columns.TryGetValue(parameter.Name, out var column))
            {
                result.Add(
                    parameter.Name,
                    new(
                        ColumnValueRetrieval.Create(column, parameter.ParameterType, this._scalarConverter, strategy.IsNonNullableReferenceType(parameter)),
                        new ConstructorParameterBindingTarget(parameter)
                    )
                );
            }
            else if (!parameter.HasDefaultValue)
            {
                return null;
            }
        }

        return result;
    }

    private void BindPropertiesAndFields(
        IReadOnlyList<(string Name, Column Column)> columns,
        PocoTypeMappingStrategy strategy, 
        Dictionary<string, ColumnBinding> bindings)
    {
        if (bindings.Count == columns.Count) { return; } // short-circuit

        foreach (var (name, column) in columns)
        {
            if (!bindings.ContainsKey(name) && strategy.NameMapping.TryGetValue(name, out var members))
            {
                var bestMatch = members.Select(m => m.Member)
                    .Where(m => m != null) // not a parameter
                    .OrderBy(m => m is PropertyInfo ? 0 : 1) // prefer properties
                    .ThenByDescending(m => m!.Name == name) // break ties with exact case matches
                    .FirstOrDefault();
                if (bestMatch != null)
                {
                    bindings.Add(
                        name,
                        bestMatch switch
                        {
                            PropertyInfo property =>
                                new ColumnBinding(
                                    ColumnValueRetrieval.Create(column, property.PropertyType, this._scalarConverter, strategy.IsNonNullableReferenceType(property)),
                                    new PropertyBindingTarget(property)
                                ),
                            FieldInfo field =>
                                new ColumnBinding(
                                    ColumnValueRetrieval.Create(column, field.FieldType, this._scalarConverter, strategy.IsNonNullableReferenceType(field)),
                                    new FieldBindingTarget(field)
                                ),
                            _ => throw Invariant.ShouldNeverGetHere()
                        }
                    );
                }
            }

            // TODO check for nested
        }
    }
}
