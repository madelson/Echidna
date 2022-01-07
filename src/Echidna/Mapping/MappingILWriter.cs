using System.Reflection;
using System.Reflection.Emit;
using static System.Reflection.Emit.OpCodes;

namespace Medallion.Data.Mapping;

internal class MappingILWriter : ILWriter
{
    private readonly Type _readerType;

    /// <summary>
    /// True if <see cref="_readerType"/> will be the result of calling <see cref="this.GetType()"/> on the <see cref="DbDataReader"/> at runtime.
    /// </summary>
    private readonly bool _isExactReaderType;

    public MappingILWriter(
        ILGenerator il, 
        Type readerType,
        bool isExactReaderType) : base(il)
    {
        Invariant.Require(!readerType.IsAbstract || !isExactReaderType);

        this._readerType = readerType;
        this._isExactReaderType = isExactReaderType;
    }

    public LocalBuilder? CurrentColumnVariable { get; private set; }

    public void Emit(ColumnValueRetrieval retrieval)
    {
        // If we're doing column-based error handling, update the indicator variable to
        // point to the current column index
        if (this.CurrentColumnVariable != null)
        {
            EmitPushColumnIndex(); // stack is [index]
            this.IL.Emit(Stloc, this.CurrentColumnVariable!); // stack is []
        }

        // TODO INullable types?
        
        var readerMethods = MappingMethods.ForReaderType(this._readerType);
        var callReaderMethodOpCode = this._isExactReaderType ? Call : Callvirt;
        var isDestinationNullable = !retrieval.IsDestinationNonNullableReferenceType
            && retrieval.DestinationType.CanBeNull();

        this.EmitPushReader(); // stack is [reader]
        var isNotNullLabel = this.IL.DefineLabel();
        var doneLabel = this.IL.DefineLabel();

        // For object, we use reader.GetValue() which returns DBNull.Value for nulls. Therefore:
        //
        // Non-nullable destination:
        //  var value = reader.GetValue(index);
        //  if (value == DBNull.Value) { throw; }
        // 
        // Nullable destination:
        //  var value = reader.GetValue(index);
        //  if (value == DBNull.Value) { value = null; }
        if (retrieval.RetrieveAsType == typeof(object))
        {
            EmitPushColumnIndex(); // stack is [reader, index]
            this.IL.Emit(callReaderMethodOpCode, readerMethods.GetValueMethod); // stack is [value]
            this.IL.Emit(Dup); // stack is [value, value]
            this.IL.Emit(Ldsfld, MappingMethods.DBNullValueField); // stack is [value, value, DBNull.Value]
            this.IL.Emit(Bne_Un, isNotNullLabel); // stack is [value]

            // null case
            if (isDestinationNullable)
            {
                this.IL.Emit(Pop); // stack is []
                EmitPushNullValueOfDestinationType(); // stack is [null]
                this.IL.Emit(Br, doneLabel);
            }
            else
            {
                this.IL.Emit(Newobj, MappingMethods.SqlNullValueExceptionDefaultConstructor);
                this.IL.Emit(Throw);
            }

            // not null case
            this.IL.MarkLabel(isNotNullLabel);
        }
        // Other get methods like reader.GetInt32() and reader.GetFieldValue<T>() throw if the value is null. Therefore:
        //
        // Non-nullable destination:
        //  var value = reader.GetInt32(index);
        // 
        // Nullable destination:
        //  var value = reader.IsDBNull(index) : null : reader.GetInt32(index);
        else
        {
            if (isDestinationNullable)
            {
                this.IL.Emit(Dup); // stack is [reader, reader]
                EmitPushColumnIndex(); // stack is [reader, reader, index]
                this.IL.Emit(callReaderMethodOpCode, readerMethods.IsDBNullMethod); // stack is [reader, isNull]
                this.IL.Emit(Brfalse, isNotNullLabel); // stack is [reader]

                // null case
                this.IL.Emit(Pop); // stack is []
                EmitPushNullValueOfDestinationType(); // stack is [null]
                this.IL.Emit(Br, doneLabel);
            }

            // not null case
            this.IL.MarkLabel(isNotNullLabel);
            EmitPushColumnIndex(); // stack is [reader, index]
            var getMethod = readerMethods.TypedGetMethods.TryGetValue(retrieval.RetrieveAsType, out var typedGetMethod)
                ? typedGetMethod
                : readerMethods.GetFieldValueGenericMethodDefinition.MakeGenericMethod(retrieval.RetrieveAsType);
            this.IL.Emit(callReaderMethodOpCode, getMethod); // stack is [value]
        }

        retrieval.Conversion?.WriteConversion(this); // stack is [value]
        this.IL.MarkLabel(doneLabel);

        // helpers

        void EmitPushColumnIndex() => this.IL.Emit(Ldc_I4, retrieval.Column.Index);

        void EmitPushNullValueOfDestinationType()
        {
            Invariant.Require(retrieval.DestinationType.CanBeNull());

            if (retrieval.DestinationType.IsValueType) // Nullable<T>
            {
                using var nullableNull = this.UseLocal(retrieval.DestinationType);
                this.IL.Emit(Ldloca, nullableNull); // stack is [&nullableNull]
                this.IL.Emit(Initobj, retrieval.DestinationType); // stack is [reader]
                this.IL.Emit(Ldloc, nullableNull); // stack is [default(Nullable<T>)]
            }
            else // reference type
            {
                this.IL.Emit(Ldnull); // stack is [null]
            }
        }
    }

    public void EmitPushReader() => this.IL.Emit(Ldarg_0);

    public CurrentColumnIndexScope UseCurrentColumnIndexLocal()
    {
        var localScope = this.UseLocal(typeof(int));
        this.CurrentColumnVariable = localScope;
        return new(this, localScope);
    }

    public ref struct CurrentColumnIndexScope
    {
        private MappingILWriter? _writer;
        private LocalScope _scope;

        public CurrentColumnIndexScope(MappingILWriter writer, LocalScope scope)
        {
            this._writer = writer;
            this._scope = scope;
        }

        public void Dispose()
        {
            var writer = Interlocked.Exchange(ref this._writer, null);
            if (writer != null)
            {
                Invariant.Require(writer.CurrentColumnVariable == (LocalBuilder)this._scope);
                writer.CurrentColumnVariable = null;
                this._scope.Dispose();
            }
        }
    }
}
