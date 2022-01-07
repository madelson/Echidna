using System.Diagnostics.CodeAnalysis;

namespace Medallion.Data.Mapping;

internal interface ITypeMapper
{
    bool TryBind(RowSchema schema, Type destination, [NotNullWhen(returnValue: true)] out List<ColumnBinding>? bindings);

    bool TryCreateMapperWriter(IReadOnlyCollection<ColumnBinding> bindings, [NotNullWhen(returnValue: true)] out RowMapperWriter? writer);
}
