using System.Reflection.Emit;

namespace Medallion.Data.Mapping;

/// <summary>
/// Provides some tools on top of <see cref="ILGenerator"/> to make writing IL a bit easier
/// </summary>
internal class ILWriter
{
    // maps locals to whether they are in use
    private readonly Dictionary<LocalBuilder, bool> _locals = new();

    public ILWriter(ILGenerator il)
    {
        this.IL = il;
    }

    public ILGenerator IL { get; }

    public LocalScope UseLocal(Type type)
    {
        var local = this._locals.FirstOrDefault(kvp => !kvp.Value && kvp.Key.LocalType == type).Key;
        if (local == null)
        {
            local = this.IL.DeclareLocal(type);
            Invariant.Require(local.LocalIndex == this._locals.Count, "all locals should be declared with UseLocal()");
            this._locals.Add(local, true);
        }
        else // reuse existing
        {
            this._locals[local] = true;
        }

        return new LocalScope(this, local);
    }

    public ref struct LocalScope
    {
        private readonly LocalBuilder _local;
        private ILWriter _writer;

        public LocalScope(ILWriter writer, LocalBuilder local)
        {
            this._writer = writer;
            this._local = local;
        }

        public void Dispose()
        {
            var writer = Interlocked.Exchange(ref this._writer!, null);
            if (writer != null)
            {
                Invariant.Require(writer._locals[this._local]);
                writer._locals[this._local] = false;
            }
        }

        public static implicit operator LocalBuilder(LocalScope scope)
        {
            Invariant.Require(scope._writer != null);
            return scope._local;
        }
    }
}
