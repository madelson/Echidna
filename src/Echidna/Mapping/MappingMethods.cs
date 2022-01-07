using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data.Common;
using System.Data.SqlTypes;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Medallion.Data.Mapping;

internal static class MappingMethods
{
    private static readonly ConcurrentDictionary<Type, DbDataReaderMethods> DbDataReaderMethodsByType = new();

    public static readonly MethodInfo DateTimeToDateOnly,
        TimeSpanToTimeOnly;

    public static readonly IReadOnlyDictionary<(Type From, Type To), MethodInfo> ConvertMethods;

    public static readonly ConstructorInfo LossyConversionExceptionConstructor,
        ArgumentOutOfRangeExceptionConstructor,
        ColumnMappingExceptionConstructor,
        MappingExceptionConstructor,
        SqlNullValueExceptionDefaultConstructor;

    public static readonly PropertyInfo StringComparerOrdinalIgnoreCaseProperty;

    public static readonly FieldInfo DBNullValueField;

    static MappingMethods()
    {
        var mappingHelpersMethods = typeof(MappingHelpers).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .ToDictionary(m => m.Name);
        var dbDataReaderMethods = typeof(DbDataReader).GetMethods(BindingFlags.Public | BindingFlags.Instance);

        DateTimeToDateOnly = mappingHelpersMethods[nameof(MappingHelpers.ToDateOnly)];
        TimeSpanToTimeOnly = typeof(TimeOnly).GetMethod(nameof(TimeOnly.FromTimeSpan), BindingFlags.Public | BindingFlags.Static, new[] { typeof(TimeSpan) })!;
        
        ConvertMethods = typeof(Convert).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Select(m => (Method: m, Parameters: m.GetParameters()))
            .Where(t => t.Parameters.Length == 1 && t.Method.Name == "To" + t.Method.ReturnType.Name)
            .ToDictionary(t => (t.Parameters[0].ParameterType, t.Method.ReturnType), t => t.Method);
        
        LossyConversionExceptionConstructor = typeof(LossyConversionException).GetConstructor(Type.EmptyTypes)!;
        ArgumentOutOfRangeExceptionConstructor = typeof(ArgumentOutOfRangeException).GetConstructor(Type.EmptyTypes)!;
        ColumnMappingExceptionConstructor = typeof(ColumnMappingException).GetConstructors().Single();
        MappingExceptionConstructor = typeof(MappingException).GetConstructors().Single();
        SqlNullValueExceptionDefaultConstructor = typeof(SqlNullValueException).GetConstructor(Type.EmptyTypes)!;

        StringComparerOrdinalIgnoreCaseProperty = typeof(StringComparer).GetProperty(nameof(StringComparer.OrdinalIgnoreCase), BindingFlags.Public | BindingFlags.Static)!;

        DBNullValueField = typeof(DBNull).GetField(nameof(DBNull.Value), BindingFlags.Public | BindingFlags.Static)!;
    }

    public static DbDataReaderMethods ForReaderType(Type readerType) =>
        DbDataReaderMethodsByType.GetOrAdd(readerType, static t => new DbDataReaderMethods(t));

    private static bool HasSingleInt32Parameter(MethodInfo method)
    {
        var parameters = method.GetParameters();
        return parameters.Length == 1 && parameters[0].ParameterType == typeof(int);
    }

    public sealed class DbDataReaderMethods
    {
        public IReadOnlyDictionary<Type, MethodInfo> TypedGetMethods { get; }
        public MethodInfo GetValueMethod { get; }
        public MethodInfo GetFieldValueGenericMethodDefinition { get; }
        public MethodInfo IsDBNullMethod { get; }

        public DbDataReaderMethods(Type readerType)
        {
            Invariant.Require(typeof(DbDataReader).IsAssignableFrom(readerType));

            var readerMethods = readerType.GetMethods(BindingFlags.Public | BindingFlags.Instance);

            this.TypedGetMethods = readerMethods
                .Where(m => m.Name.StartsWith("Get") && m.Name == "Get" + m.ReturnType.Name && HasSingleInt32Parameter(m))
                .ToDictionary(m => m.ReturnType);
            this.GetValueMethod = readerMethods.First(m => m.Name == nameof(DbDataReader.GetValue) && m.ReturnType == typeof(object) && HasSingleInt32Parameter(m));
            this.GetFieldValueGenericMethodDefinition = readerMethods.First(
                m => m.Name == nameof(DbDataReader.GetFieldValue) 
                    && m.IsGenericMethodDefinition
                    && m.GetGenericArguments().SequenceEqual(new[] { m.ReturnType })
                    && HasSingleInt32Parameter(m)
            );
            this.IsDBNullMethod = readerMethods.First(m => m.Name == nameof(DbDataReader.IsDBNull) && m.ReturnType == typeof(bool) && HasSingleInt32Parameter(m));
        }
    }
}
