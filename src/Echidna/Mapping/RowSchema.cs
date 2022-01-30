using System.Collections.Immutable;
using System.Data.Common;

namespace Medallion.Data.Mapping;

internal sealed class RowSchema : IEquatable<RowSchema>
{
    private RowSchema(ImmutableArray<Type> columnTypes, ImmutableArray<string> columnNames)
    {
        Invariant.Require(columnTypes.Length == columnNames.Length);
        this.ColumnTypes = columnTypes;
        this.ColumnNames = columnNames;
    }

    public static RowSchema FromReader(DbDataReader reader)
    { 
        var count = reader.VisibleFieldCount;
        var types = ImmutableArray.CreateBuilder<Type>(initialCapacity: count);
        var names = ImmutableArray.CreateBuilder<string>(initialCapacity: count);

        for (var i = 0; i < count; ++i)
        {
            types.Add(reader.GetFieldType(i));
            names.Add(reader.GetName(i));
        }
        return new(types.MoveToImmutable(), names.MoveToImmutable());
    }

    public Column this[int index] => new(index, this.ColumnNames[index], this.ColumnTypes[index]);

    public ImmutableArray<Type> ColumnTypes { get; }

    public ImmutableArray<string> ColumnNames { get; }

    public int ColumnCount => this.ColumnTypes.Length;

    public override bool Equals(object? obj) =>
        obj is RowSchema that && this.EqualsNonNullable(that);

    public bool Equals(RowSchema? that) => that != null && this.EqualsNonNullable(that);

    private bool EqualsNonNullable(RowSchema that) =>
        this.ColumnTypes.AsSpan().SequenceEqual(that.ColumnTypes.AsSpan())
            && this.ColumnNames.AsSpan().SequenceEqual(that.ColumnNames.AsSpan());

    public override int GetHashCode()
    {
        var hashCode = default(HashCode);
        foreach (var type in this.ColumnTypes.AsSpan()) { hashCode.Add(type); }
        foreach (var name in this.ColumnNames.AsSpan()) { hashCode.Add(name); }
        return hashCode.ToHashCode();
    }

    public List<(string Name, Column Column)> GetColumns(string prefix, Range range)
    {
        var result = new List<(string Name, Column Column)>();
        var (offset, length) = range.GetOffsetAndLength(this.ColumnCount);
        for (var i = 0; i < length; ++i)
        {
            var column = this[i + offset];
            if (column.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                result.Add((column.Name.Substring(prefix.Length), column));
            }
        }
        return result;
    }
}
