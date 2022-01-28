using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Medallion.Data;

internal abstract class InternalMiddlewareContext 
{
    private readonly IReadOnlyList<Func<EchidnaMiddlewareContext, ValueTask<object?>>> _middleware;
    private readonly OneOf<DbCommand, DbBatch> _command;
    private Dictionary<object, object?>? _data;

    protected InternalMiddlewareContext(
        IReadOnlyList<Func<EchidnaMiddlewareContext, ValueTask<object?>>> middleware,
        OneOf<DbCommand, DbBatch> command, 
        bool isAsync)
    {
        this._middleware = middleware;
        this._command = command;
        this.IsAsync = IsAsync;
    }

    public DbCommand? Command => this._command.First;
    public DbBatch? Batch => this._command.Second;
    public bool IsAsync { get; }

    public IDictionary<object, object?> Data => this._data ??= new();

    protected abstract ValueTask<object?> InternalExecuteAsync();

    public ValueTask<object?> NextAsync(int index)
    {
        var middleware = this._middleware;
        return index < middleware.Count
            ? middleware[index](new(this, index + 1))
            : this.InternalExecuteAsync();
    }

    public ValueTask<object?> ExecutePipelineAsync() => this.NextAsync(0);
}

// todo move
internal readonly struct OneOf<TEither, TOr> 
    where TEither : class
    where TOr : class
{
    private readonly object _value;

    private OneOf(object value) { this._value = value; }

    public TEither? First => this._value as TEither;
    public TOr? Second => this._value as TOr;

    public static implicit operator OneOf<TEither, TOr>(TEither value) => new(value);
    public static implicit operator OneOf<TEither, TOr>(TOr value) => new(value);
}

public struct EchidnaMiddlewareContext
{
    private readonly InternalMiddlewareContext _context;
    private int _index;

    internal EchidnaMiddlewareContext(InternalMiddlewareContext context, int index) 
    {
        this._context = context;
        this._index = index;
    }

    public DbCommand? Command => this._context.Command;
    public DbBatch? Batch => this._context.Batch;
    public bool IsAsync => this._context.IsAsync;
    public IDictionary<object, object?> Data => this._context.Data;

    public ValueTask<object?> NextAsync() => this._context.NextAsync(this._index);
}
