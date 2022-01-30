namespace Medallion.Data.Mapping;

internal record MappingResult(Action<MappingILWriter, ColumnLoader> Emit, IReadOnlyList<ColumnBinding> Bindings, bool IsPartialBinding);

