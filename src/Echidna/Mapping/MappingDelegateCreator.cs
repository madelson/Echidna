using System.Data.Common;
using System.Reflection;
using System.Reflection.Emit;
using static System.Reflection.Emit.OpCodes;

namespace Medallion.Data.Mapping;

internal static class MappingDelegateCreator
{
    public static Delegate CreateMappingDelegate(Type readerType, RowSchema schema, Type destinationType)
    {
        var dynamicMethod = new DynamicMethod(
            name: $"Map_{readerType}_To_{destinationType}_{unchecked((uint)schema.GetHashCode()):x}",
            returnType: destinationType,
            parameterTypes: new[] { readerType },
            // https://devblogs.microsoft.com/premier-developer/dissecting-the-new-constraint-in-c-a-perfect-example-of-a-leaky-abstraction/
            // suggests that specifying a module increases perf
            m: typeof(MappingDelegateCreator).Module,
            // lets us call non-public methods
            skipVisibility: true)
        {
            InitLocals = false // not needed; should help perf slightly
        };

        RowMapperWriter? rowMapperWriter;
        var dictionaryMapper = new DictionaryMapper(); // TODO
        if (dictionaryMapper.TryBind(schema, destinationType, out var bindings))
        {
            if (!dictionaryMapper.TryCreateMapperWriter(bindings, out rowMapperWriter))
            {
                throw Invariant.ShouldNeverGetHere();
            }
        }
        else
        {
            // todo clean this up
            throw new InvalidOperationException($"Could not generate mapping for type {destinationType}");
        }

        WriteIL();

        var delegateType = typeof(Func<,>).MakeGenericType(readerType, destinationType);
        return dynamicMethod.CreateDelegate(delegateType);

        void WriteIL()
        {
            var writer = new MappingILWriter(
                dynamicMethod.GetILGenerator(),
                readerType
            );

            using var _ = rowMapperWriter.Bindings.Any(b => !b.Retrieval.IsSafe) ? writer.UseCurrentColumnIndexLocal() : default;

            // var currentColumnIndex = -1
            if (writer.CurrentColumnVariable != null)
            {
                writer.IL.Emit(Ldc_I4_M1); // stack is [-1]
                writer.IL.Emit(Stloc, writer.CurrentColumnVariable); // stack is []
            }

            // TODO we can simplify this by generating a Func<reader, ref i, T> and doing the error handling in non-generated code
            // (in our MappingDelegateProvider for example). In that case this class should also return an exception handler
            var exceptionBlock = writer.IL.BeginExceptionBlock();

            rowMapperWriter.EmitMappingLogic(writer); // stack is [result]
            using var result = writer.UseLocal(destinationType);
            writer.IL.Emit(Stloc, result);
            writer.IL.Emit(Leave, exceptionBlock);

            writer.IL.BeginCatchBlock(typeof(Exception)); // stack is [ex]
            var throwLabel = writer.IL.DefineLabel();
            if (writer.CurrentColumnVariable != null)
            {
                writer.IL.Emit(Ldloc, writer.CurrentColumnVariable!); // stack is [ex, currentId]

                var remainingUnsafeBindings = new Queue<ColumnBinding>(rowMapperWriter.Bindings.Where(b => !b.Retrieval.IsSafe).OrderBy(b => b.Retrieval.Column.Index));
                while (remainingUnsafeBindings.TryDequeue(out var binding))
                {
                    if (remainingUnsafeBindings.Count != 0) { writer.IL.Emit(Dup); } // stack is [ex, currentId, [currentId]]
                    writer.IL.Emit(Ldc_I4, binding.Retrieval.Column.Index); // stack is [ex, currentId, [currentId], id]
                    var nextConditionLabel = writer.IL.DefineLabel();
                    writer.IL.Emit(Bne_Un, nextConditionLabel); // stack is [ex, [currentId]]
                    if (remainingUnsafeBindings.Count != 0) { writer.IL.Emit(Pop); } // stack is [ex]
                    writer.EmitPushReader(); // stack is [ex, reader]
                    writer.IL.Emit(Ldc_I4, binding.Retrieval.Column.Index); // stack is [ex, reader, index]
                    writer.IL.Emit(Ldstr, binding.Target.ToString()); // stack is [ex, reader, index, descriptor]
                    writer.IL.Emit(Newobj, MappingMethods.ColumnMappingExceptionConstructor); // stack is [ex]
                    writer.IL.Emit(Br, throwLabel);
                    writer.IL.MarkLabel(nextConditionLabel);
                }
            }

            writer.IL.Emit(Ldstr, destinationType.ToString()); // stack is [ex, descriptor]
            writer.IL.Emit(Newobj, MappingMethods.MappingExceptionConstructor); // stack is [ex]
            writer.IL.MarkLabel(throwLabel);
            writer.IL.Emit(Throw);

            writer.IL.EndExceptionBlock();

            writer.IL.Emit(Ldloc, result);
            writer.IL.Emit(Ret);
        }
    }
}
