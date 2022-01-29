using System.Reflection;
using System.Reflection.Emit;
using static System.Reflection.Emit.OpCodes;

namespace Medallion.Data.Mapping;

internal class MappingILWriter : ILWriter
{
    // 0 is unused target
    private const int ReaderArgumentIndex = 1;
    private const int ColumnIndexRefArgumentIndex = 2;

    private readonly Type _readerType;

    public MappingILWriter(ILGenerator il, Type readerType) : base(il)
    {
        Invariant.Require(!readerType.IsAbstract);

        this._readerType = readerType;
    } 

    public void Emit(ColumnValueRetrieval retrieval)
    {
        // Update the indicator variable to point to the current column index
        this.IL.Emit(Ldarg, ColumnIndexRefArgumentIndex); // stack is [&columnIndex]
        EmitPushColumnIndex(); // stack is [&columnIndex, index]
        this.IL.Emit(Stind_I4); // stack is []

        // TODO INullable types?
        
        var readerMethods = MappingMethods.ForReaderType(this._readerType);
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
            this.IL.Emit(Call, readerMethods.GetValueMethod); // stack is [value]
            this.IL.Emit(Dup); // stack is [value, value]
            this.IL.Emit(Ldsfld, MappingMethods.DBNullValueField); // stack is [value, value, DBNull.Value]
            this.IL.Emit(Bne_Un, isNotNullLabel); // stack is [value]

            // null case
            if (isDestinationNullable)
            {
                this.IL.Emit(Pop); // stack is []
                this.EmitLoadConstant(retrieval.DestinationType, null); // stack is [null]
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
                this.IL.Emit(Call, readerMethods.IsDBNullMethod); // stack is [reader, isNull]
                this.IL.Emit(Brfalse, isNotNullLabel); // stack is [reader]

                // null case
                this.IL.Emit(Pop); // stack is []
                this.EmitLoadConstant(retrieval.DestinationType, null); // stack is [null]
                this.IL.Emit(Br, doneLabel);
            }

            // not null case
            this.IL.MarkLabel(isNotNullLabel);
            EmitPushColumnIndex(); // stack is [reader, index]
            var getMethod = readerMethods.TypedGetMethods.TryGetValue(retrieval.RetrieveAsType, out var typedGetMethod)
                ? typedGetMethod
                : readerMethods.GetFieldValueGenericMethodDefinition.MakeGenericMethod(retrieval.RetrieveAsType);
            this.IL.Emit(Call, getMethod); // stack is [value]
        }

        retrieval.Conversion?.WriteConversion(this); // stack is [value]
        this.IL.MarkLabel(doneLabel);

        // helpers

        void EmitPushColumnIndex() => this.IL.Emit(Ldc_I4, retrieval.Column.Index);
    }

    public void EmitPushReader() => this.IL.Emit(Ldarg, ReaderArgumentIndex);
}
