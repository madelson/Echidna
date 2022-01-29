using System.Reflection.Emit;
using static System.Reflection.Emit.OpCodes;

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

    public void EmitLoadConstant(Type type, object? value)
    {
        var il = this.IL;

        if (!type.IsValueType)
        {
            if (value == null)
            {
                il.Emit(Ldnull);
                return;
            }

            if (value is string @string)
            {
                Invariant.Require(type.IsAssignableFrom(typeof(string)));
                il.Emit(Ldstr, @string);
                return;
            }
        }
        else
        {
            var underlyingType = Nullable.GetUnderlyingType(type);
            if (underlyingType != null)
            {
                if (value is null)
                {
                    using var nullableNull = this.UseLocal(type);
                    il.Emit(Ldloca, nullableNull); // stack is [&nullableNull]
                    il.Emit(Initobj, type); // stack is []
                    il.Emit(Ldloc, nullableNull); // stack is [default(Nullable<T>)]
                    return;
                }

                this.EmitLoadConstant(underlyingType, value); // stack is [value]
                il.Emit(Newobj, type.GetConstructor(new[] { underlyingType })!); // stack is [Nullable<T>(value)]
            }

            Invariant.Require(type == value!.GetType());

            switch (value)
            {
                case bool @boolean:
                    il.Emit(Ldc_I4, Convert.ToInt32(@boolean));
                    return;
                case sbyte @sbyte:
                    il.Emit(Ldc_I4, (int)@sbyte);
                    return;
                case byte @byte:
                    il.Emit(Ldc_I4, (int)@byte);
                    return;
                case short @short:
                    il.Emit(Ldc_I4, (int)@short);
                    return;
                case ushort @ushort:
                    il.Emit(Ldc_I4, (int)@ushort);
                    return;
                case int @int:
                    il.Emit(Ldc_I4, @int);
                    return;
                case uint @uint:
                    il.Emit(Ldc_I4, unchecked((int)@uint));
                    return;
                case long @long:
                    il.Emit(Ldc_I8, @long);
                    return;
                case ulong @ulong:
                    il.Emit(Ldc_I4, unchecked((long)@ulong));
                    return;
                case float @float:
                    il.Emit(Ldc_R4, @float);
                    break;
                case double @double:
                    il.Emit(Ldc_R8, @double);
                    return;
                case decimal @decimal:
                    if (@decimal == 0)
                    {
                        il.Emit(Ldsfld, MappingMethods.DecimalZeroField);
                    }
                    else if (int.MinValue <= @decimal && @decimal <= int.MaxValue)
                    {
                        this.EmitLoadConstant(typeof(int), (int)@decimal);
                        il.Emit(Call, MappingMethods.DecimalFromInt32Constructor);
                    }
                    else if (long.MinValue <= @decimal && @decimal <= long.MinValue)
                    {
                        this.EmitLoadConstant(typeof(long), (long)@decimal);
                        il.Emit(Call, MappingMethods.DecimalFromInt64Constructor);
                    }
                    else if (ulong.MinValue <= @decimal && @decimal <= ulong.MaxValue)
                    {
                        this.EmitLoadConstant(typeof(ulong), (ulong)@decimal);
                        il.Emit(Call, MappingMethods.DecimalFromUInt64Constructor);
                    }
                    // general case based on https://stackoverflow.com/questions/33570554/emit-il-code-to-load-a-decimal-value
                    else
                    {
                        var bits = decimal.GetBits(@decimal);
                        this.EmitLoadConstant(typeof(int), bits[0]);
                        this.EmitLoadConstant(typeof(int), bits[1]);
                        this.EmitLoadConstant(typeof(int), bits[2]);
                        var sign = (bits[3] & 0x80000000) != 0;
                        this.EmitLoadConstant(typeof(bool), sign);
                        var scale = (byte)((bits[3] >> 16) & 0x7f);
                        this.EmitLoadConstant(typeof(byte), scale);
                    }
                    return;
            }
        }

        throw new NotImplementedException();
    }

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
