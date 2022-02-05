using System.Data.Common;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
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

        var strategy = CompositeTypeMappingStrategy.TryCreateFor(destinationType, out var compositeStrategy, out var errorMessage)
            ? compositeStrategy
            : throw new NotImplementedException(); // todo use scalar for single column

        MappingResult? mappingResult;
        // todo we should get the mapper from the strategy rather than switching
        switch (strategy)
        {
            case DictionaryTypeMappingStrategy dictionaryStrategy:
                mappingResult = new DictionaryMapper().Map(dictionaryStrategy, schema, prefix: string.Empty, Range.All);
                break;
            case PocoTypeMappingStrategy pocoStrategy:
                mappingResult = new PocoMapper().TryMap(pocoStrategy, schema, prefix: string.Empty, Range.All);
                break;
            default:
                throw new NotImplementedException(); // todo
        }
        if (mappingResult is null)
        {
            // todo if single column use scalar result
            // todo get message out of the mapper ideally
            throw new NotImplementedException();
        }
        ValidateMappingResult(mappingResult, schema, destinationType);

        var writer = new MappingILWriter(
            dynamicMethod.GetILGenerator(),
            readerType
        );
        mappingResult.Emit(writer, new ColumnLoader(writer, mappingResult.Bindings.Select(b => b.Retrieval))); // stack is [result]
        writer.IL.Emit(Ret);

        var delegateType = typeof(MappingDelegate<,>).MakeGenericType(readerType, destinationType);
        // note: using a target makes delegates faster!
        // TODO later we might bind to the RowSchema or similar to power a Row type
        return new(dynamicMethod.CreateDelegate(delegateType, target: null), new MappingDelegateErrorHandler(mappingResult.Bindings, destinationType));
    }

    private static void ValidateMappingResult(MappingResult result, RowSchema schema, Type destinationType)
    {
        if (result.IsPartialBinding)
        {
            var unboundColumns = Enumerable.Range(0, schema.ColumnCount)
                .Except(result.Bindings.Select(b => b.Retrieval.Column.Index))
                .ToArray();
            if (unboundColumns.Length != 0)
            {
                var errorBuilder = new StringBuilder()
                    .AppendLine($"Failed to map result set to {destinationType} because not all columns were bound and {destinationType} could only be partially populated.")
                    .AppendLine($"The following columns were not bound: {string.Join(", ", unboundColumns.Select(i => schema[i]))}")
                    .AppendLine("The following columns were bound:");
                var bindingsByColumnIndex = result.Bindings.ToLookup(b => b.Retrieval.Column.Index);
                foreach (var group in bindingsByColumnIndex)
                {
                    foreach (var binding in group)
                    {
                        errorBuilder.AppendLine($"\t{binding.Retrieval.Column} -> {binding.Target}");
                    }
                }
                throw new MappingException(errorBuilder.ToString());
            }
        }
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
