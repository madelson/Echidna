namespace Medallion.Data.Mapping;

internal sealed record RowMapperWriter(Action<MappingILWriter> EmitMappingLogic, IReadOnlyCollection<ColumnBinding> Bindings);
