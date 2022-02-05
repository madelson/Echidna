using System.Data;
using System.Data.Common;
using static System.Reflection.Emit.OpCodes;

namespace Medallion.Data.Mapping;

/// <summary>
/// <see cref="DbDataReader"/>s using <see cref="CommandBehavior.SequentialAccess"/> must have columns accessed in order.
/// However, in many cases we might need columns out of order (e. g. to load constructor parameters for a POCO).
/// <see cref="ColumnLoader"/> solves for this by loading all relevant columns in sequential order behind the scenes
/// as we load them; automatically storing them in locals if they won't be needed immediately.
/// </summary>
internal sealed class ColumnLoader
{
    // NOTE: this implementation assumes that each bound column is loaded exactly once in the IL
    // i. e. there are not forks in the logic where columns are loaded conditionally or differently
    // on the different forks.

    private readonly MappingILWriter _writer;
    private readonly IReadOnlyList<ColumnValueRetrieval> _columns;
    private readonly Dictionary<ColumnValueRetrieval, int> _columnsToOrdinals;
    private readonly Dictionary<ColumnValueRetrieval, ILWriter.LocalScope> _storedColumns = new();
    private int _nextOrdinal;

    public ColumnLoader(MappingILWriter writer, IEnumerable<ColumnValueRetrieval> columns)
    {
        this._writer = writer;
        this._columns = columns.OrderBy(c => c.Column.Index).ToArray();
        this._columnsToOrdinals = this._columns 
            .Select((c, index) => (Column: c, Ordinal: index))
            .ToDictionary(t => t.Column, t => t.Ordinal);
    }

    public void EmitLoad(ColumnValueRetrieval column)
    {
        // we've already stored it off, so just load the local
        if (this._storedColumns.Remove(column, out var existingLocal))
        {
            this._writer.IL.Emit(Ldloc, existingLocal);
            existingLocal.Dispose();
            return;
        }

        var ordinal = this._columnsToOrdinals[column];

        // load any columns BEFORE this one and store them in locals
        while (this._nextOrdinal < ordinal)
        {
            var precedingColumn = this._columns[this._nextOrdinal++];
            Invariant.Require(this._columnsToOrdinals.Remove(precedingColumn), "Preceding column already requested");
            this._writer.Emit(precedingColumn);
            var local = this._writer.UseLocal(precedingColumn.DestinationType);
            this._writer.IL.Emit(Stloc, local);
            this._storedColumns.Add(precedingColumn, local);
        }

        Invariant.Require(this._columnsToOrdinals.Remove(column), "Preceding column already requested");
        this._writer.Emit(column);
        ++this._nextOrdinal;
    }

    // TODO MappingILWriter.Emit() belongs here and then maybe we can kill MappingILWriter?
}
