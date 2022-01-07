using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Medallion.Data.Mapping;

internal sealed class ColumnMappingException : Exception
{
    public ColumnMappingException(Exception innerException, DbDataReader reader, int columnIndex, string destinationDescriptor)
        : base($"Could not map column {columnIndex} ({reader.GetFieldType(columnIndex)} {reader.GetName(columnIndex)}){GetValueString(reader, columnIndex)} to {destinationDescriptor}. See inner exception for details.", innerException)
    {
    }


    private static string GetValueString(DbDataReader reader, int columnIndex)
    {
        object value;
        try { value = reader.GetValue(columnIndex); }
        // will throw for CommandBehavior.SequentialAccess. In that case we simply don't capture the original column value
        catch { return string.Empty; }

        return $" value '{(value == null || value == DBNull.Value ? "NULL" : value)}'";
    }
}
