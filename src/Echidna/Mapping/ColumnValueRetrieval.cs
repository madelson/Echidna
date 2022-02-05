using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Medallion.Data.Mapping;

internal sealed record ColumnValueRetrieval(
    Column Column,
    Type RetrieveAsType, 
    Type DestinationType,
    bool IsDestinationNonNullableReferenceType = false,
    ScalarConverter.Conversion? Conversion = null)
{
    public static ColumnValueRetrieval Create(
        Column column,
        Type destinationType,
        ScalarConverter scalarConverter,
        bool isDestinationNonNullableReferenceType = false)
    {
        Invariant.Require(!isDestinationNonNullableReferenceType || !destinationType.IsValueType);

        return column.Type != destinationType && scalarConverter.CanConvert(column.Type, destinationType, out var conversion)
            ? new(column, column.Type, destinationType, isDestinationNonNullableReferenceType, conversion)
            : new(column, destinationType, destinationType, isDestinationNonNullableReferenceType);
    }

    private bool? _cachedIsSafe;

    // TODO rename and reverse this to CanThrow
    public bool IsSafe =>
        this._cachedIsSafe ??=
            (this.Conversion?.IsSafe ?? true)
                && this.RetrieveAsType == this.Column.Type
                && !this.IsDestinationNonNullableReferenceType
                && this.DestinationType.CanBeNull();
}

internal readonly record struct Column(int Index, string Name, Type Type)
{
    public override string ToString() => $"{this.Index} ({this.Type} {this.Name})";
}
