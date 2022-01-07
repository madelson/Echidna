namespace Medallion.Data.Tests;

internal class Scope<T> : IDisposable
{
    private readonly T _value;
    private Action? _cleanup;

    public Scope(T value, Action cleanup)
    {
        this._value = value;
        this._cleanup = cleanup;
    }

    public T Value => this._cleanup is null ? throw new ObjectDisposedException(this.GetType().Name) : this._value;

    public void Dispose()
    {
        var cleanup = Interlocked.Exchange(ref this._cleanup, null);
        if (cleanup != null)
        {
            try
            {
                if (this._value is IDisposable disposable) { disposable.Dispose(); }
            }
            finally
            {
                cleanup();
            }
        }
    }
}
