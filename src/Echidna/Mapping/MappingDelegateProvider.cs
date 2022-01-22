using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Medallion.Data.Mapping;

internal static class MappingDelegateProvider
{
    // TODO incorporate settings
    private static readonly MicroCache<(Type ReaderType, RowSchema Schema, Type DestinationType), object> Cache = new(maxCount: 10000);

    public static Func<TDestination> GetMappingDelegate<TDestination>(DbDataReader reader)
    {
        var readerType = reader.GetType(); // use this over typeof(TReader) so that we can avoid virtual calls in the delegate
        var schema = RowSchema.FromReader(reader);
        var destinationType = typeof(TDestination);

        var factory = (MappingDelegateFactory<TDestination>)Cache.GetOrAdd((readerType, schema, destinationType), static key =>
        {
            var mappingDelegateInfo = MappingDelegateCreator.CreateMappingDelegate(key.ReaderType, key.Schema, key.DestinationType);

            return Activator.CreateInstance(typeof(MappingDelegateFactory<,>).MakeGenericType(key.ReaderType, key.DestinationType), mappingDelegateInfo)!;
        });

        return factory.CreateDelegate(reader);
    }

    private abstract class MappingDelegateFactory<TDestination>
    {
        public abstract Func<TDestination> CreateDelegate(DbDataReader reader);
    }

    private sealed class MappingDelegateFactory<TReader, TDestination> : MappingDelegateFactory<TDestination>
        where TReader : DbDataReader
    {
        private readonly MappingDelegate<TReader, TDestination> _delegate;
        private readonly MappingDelegateErrorHandler _errorHandler;

        public MappingDelegateFactory(MappingDelegateInfo delegateInfo)
        {
            this._delegate = (MappingDelegate<TReader, TDestination>)delegateInfo.Delegate;
            this._errorHandler = delegateInfo.ErrorHandler;
        }

        public override Func<TDestination> CreateDelegate(DbDataReader reader)
        {
            var typedReader = (TReader)reader;
            // store in locals to avoid extra indirection through this
            var @delegate = this._delegate;
            var errorHandler = this._errorHandler;
            return () =>
            {
                var columnIndex = -1;
                try { return @delegate(typedReader, ref columnIndex); }
                catch (Exception ex) { throw errorHandler.CreateException(ex, columnIndex); }
            };
        }
    }
}
