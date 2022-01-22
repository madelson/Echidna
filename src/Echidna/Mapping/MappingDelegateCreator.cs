using System.Data.Common;
using System.Reflection;
using System.Reflection.Emit;
using static System.Reflection.Emit.OpCodes;

namespace Medallion.Data.Mapping;

internal static class MappingDelegateCreator
{
    public static MappingDelegateInfo CreateMappingDelegate(Type readerType, RowSchema schema, Type destinationType)
    {
        var dynamicMethod = new DynamicMethod(
            name: $"Map_{readerType}_To_{destinationType}_{unchecked((uint)schema.GetHashCode()):x}",
            returnType: destinationType,
            parameterTypes: new[] { typeof(object), readerType, typeof(int).MakeByRefType() },
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

        var writer = new MappingILWriter(
            dynamicMethod.GetILGenerator(),
            readerType
        );
        rowMapperWriter.EmitMappingLogic(writer); // stack is [result]
        writer.IL.Emit(Ret);

        var delegateType = typeof(MappingDelegate<,>).MakeGenericType(readerType, destinationType);
        // note: using a target makes delegates faster!
        // TODO later we might bind to the RowSchema or similar to power a Row type
        return new(dynamicMethod.CreateDelegate(delegateType, target: null), new MappingDelegateErrorHandler(bindings, destinationType));
    }
}

internal delegate TDestination MappingDelegate<TReader, TDestination>(TReader reader, ref int columnIndex) where TReader : DbDataReader;

internal sealed class MappingDelegateErrorHandler
{
    private readonly ColumnBinding[] _bindings;
    private readonly Type _destination;

    public MappingDelegateErrorHandler(IReadOnlyCollection<ColumnBinding> bindings, Type destination)
    {
        this._bindings = new ColumnBinding[bindings.Max(c => c.Retrieval.Column.Index) + 1];
        foreach (var binding in bindings)
        {
            this._bindings[binding.Retrieval.Column.Index] = binding;
        }
        this._destination = destination;
    }

    public Exception CreateException(Exception innerException, int columnIndex)
    {
        if (columnIndex < 0)
        {
            return new MappingException(innerException, $"type {this._destination}");
        }

        var binding = this._bindings[columnIndex];
        return new ColumnMappingException(innerException, columnIndex, binding.Retrieval.Column.Type, binding.Retrieval.Column.Name, binding.Target.ToString());
    }
}

internal record struct MappingDelegateInfo(Delegate Delegate, MappingDelegateErrorHandler ErrorHandler);
