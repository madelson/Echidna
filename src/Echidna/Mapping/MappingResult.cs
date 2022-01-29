namespace Medallion.Data.Mapping;

internal record MappingResult(Action<MappingILWriter> Emit, IReadOnlyList<ColumnBinding> Bindings, bool IsPartialBinding);

