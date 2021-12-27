using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using static System.Reflection.Emit.OpCodes;

namespace Medallion.Data.Mapping;

internal class ScalarConverter : IEquatable<ScalarConverter>
{
    private static readonly MicroCache<CacheKey, Conversion?> Cache = new(maxCount: 5000);

    internal static readonly IReadOnlyDictionary<Type, NumericTypeFacts> SimpleNumericTypes = new Dictionary<Type, NumericTypeFacts>() 
    {
        [typeof(sbyte)] = new(sizeof(sbyte)),
        [typeof(byte)] = new(sizeof(byte), IsUnsigned: true),
        [typeof(short)] = new(sizeof(short)),
        [typeof(ushort)] = new(sizeof(ushort), IsUnsigned: true),
        [typeof(int)] = new(sizeof(int)),
        [typeof(uint)] = new(sizeof(uint), IsUnsigned: true),
        [typeof(long)] = new(sizeof(long)),
        [typeof(ulong)] = new(sizeof(ulong), IsUnsigned: true),

        [typeof(float)] = new(sizeof(float), IsFloatingPoint: true),
        [typeof(double)] = new(sizeof(double), IsFloatingPoint: true),

        [typeof(decimal)] = new(sizeof(decimal)),
    };

    // TODO lazy?
    private static readonly IReadOnlyDictionary<string, OpCode> OpCodes = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(f => f.FieldType == typeof(OpCode))
        .Select(f => (OpCode)f.GetValue(null)!)
        .ToDictionary(c => c.Name!);

    private static readonly Lazy<IReadOnlyDictionary<(Type From, Type To), MethodInfo>> ConvertMethods = new(
        () => typeof(Convert).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Select(m => (Method: m, Parameters: m.GetParameters()))
            .Where(t => t.Parameters.Length == 1 && t.Method.Name == "To" + t.Method.ReturnType.Name)
            .ToDictionary(t => (t.Parameters[0].ParameterType, t.Method.ReturnType), t => t.Method)
    );

    public bool CanConvert(
        Type from,
        Type to,
        [NotNullWhen(returnValue: true)] out Conversion? conversion)
    {
        Invariant.Require(from != to, "should not be called for trivial conversions");

        var cacheKey = new CacheKey(from, to);
        if (Cache.TryGetValue(cacheKey, out var cached))
        {
            conversion = cached;
        }
        else
        {
            var newConversion = this.GetConversionOrDefault(from, to);
            conversion = Cache.TryAdd(cacheKey, newConversion, out cached) ? newConversion : cached;
        }

        return conversion != null;
    }

    private Conversion? GetConversionOrDefault(Type from, Type to)
    {
        // T -> object
        if (to == typeof(object))
        {
            return GetConversionToObject(from);
        }

        // T -> Nullable<V>
        var toNullableUnderlyingType = Nullable.GetUnderlyingType(to);
        if (toNullableUnderlyingType != null && Nullable.GetUnderlyingType(from) == null)
        {
            return this.GetConversionToNullableValueTypeOrDefault(from, to, toNullableUnderlyingType);
        }

        if (SimpleNumericTypes.TryGetValue(from, out var fromNumericTypeFacts))
        {
            if (SimpleNumericTypes.TryGetValue(to, out var toNumericTypeFacts))
            {
                return GetNumericTypeConversion(from, fromNumericTypeFacts, to, toNumericTypeFacts);
            }
        }

        // cases
        // primitive numeric -> safe
        // primitive numeric -> checked: double -> float, (u)long -> float/double, (u)int -> float, unsigned <-> signed
        // op_Implicit (public) on to or on from

        throw new NotImplementedException();
    }

    private static Conversion GetConversionToObject(Type from) =>
        new(from.IsValueType ? w => w.IL.Emit(Box, from) : w => { }, IsSafe: true);

    private Conversion? GetConversionToNullableValueTypeOrDefault(Type from, Type to, Type toNullableUnderlyingType)
    {
        Conversion? conversionToNullableUnderlyingType;
        if (from == toNullableUnderlyingType)
        {
            conversionToNullableUnderlyingType = null;
        }
        else if (!this.CanConvert(from, to, out conversionToNullableUnderlyingType))
        {
            return null;
        }

        return new(
            w =>
            {
                conversionToNullableUnderlyingType?.WriteConversion(w);
                w.IL.Emit(Newobj, to.GetConstructor(new[] { toNullableUnderlyingType })!);
            },
            IsSafe: conversionToNullableUnderlyingType?.IsSafe ?? true
        );
    }

    private static Conversion GetNumericTypeConversion(
        Type from,
        NumericTypeFacts fromNumericTypeFacts,
        Type to,
        NumericTypeFacts toNumericTypeFacts)
    {
        if (from == typeof(decimal))
        {
            // decimal->integral conversion
            if (!toNumericTypeFacts.IsFloatingPoint)
            {
                return GetDecimalToIntegralConversion();
            }

            return GetConversionWithRoundTripCheck();
        }

        if (to == typeof(decimal))
        {
            // integral->decimal can just use decimal's implicit conversions
            if (!fromNumericTypeFacts.IsFloatingPoint)
            {
                return GetImplicitConversionOrDefault(from, to) 
                    ?? throw Invariant.ShouldNeverGetHere();
            }

            return GetConversionWithRoundTripCheck();
        }

        // integral->integral conversions
        if (!fromNumericTypeFacts.IsFloatingPoint && !toNumericTypeFacts.IsFloatingPoint)
        {
            return GetIntegralToIntegralConversion();
        }

        // safe integral/floating point->floating point conversions
        if (toNumericTypeFacts.IsFloatingPoint && toNumericTypeFacts.Size > fromNumericTypeFacts.Size)
        {
            return GetSafeConversionToFloatingPoint();
        }

        return GetConversionWithRoundTripCheck();

        // todo revisit: this should be able to use round trip stuff
        Conversion GetDecimalToIntegralConversion()
        {
            var truncateMethod = Helpers.GetMethod(() => decimal.Truncate(default));
            var equalsMethod = GetEqualityOperatorOrDefault(typeof(decimal))!;
            var exceptionConstructor = Helpers.GetConstructor(() => new NonIntegralValueTruncatedException());
            var convertMethod = typeof(decimal).GetMethod("To" + to.Name, BindingFlags.Public | BindingFlags.Static, new[] { from })!;

            return new Conversion(
                w =>
                {
                    // first, truncate the value and make sure it does not change
                    w.IL.Emit(Dup); // stack is [from, from]
                    w.IL.Emit(Dup); // stack is [from, from, from]
                    w.IL.Emit(Call, truncateMethod); // stack is [from, from, trunc]
                    w.IL.Emit(Call, equalsMethod); // stack is [from, cmp]
                    var successLabel = w.IL.DefineLabel();
                    w.IL.Emit(Brtrue, successLabel); // stack is [from]
                    w.IL.Emit(Newobj, exceptionConstructor);
                    w.IL.Emit(Throw);
                    w.IL.MarkLabel(successLabel);

                    // then, convert the value (will throw if too large or too small)
                    w.IL.Emit(Call, convertMethod); // stack is [converted]
                },
                IsSafe: false
            );
        }

        Conversion GetConversionWithRoundTripCheck()
        {
            var convertMethod = ConvertMethods.Value[(from, to)];
            var equalsOperator = from == typeof(decimal)
                ? GetEqualityOperatorOrDefault(typeof(decimal))
                : null;
            var reverseConvertMethod = ConvertMethods.Value[(to, from)];
            var exceptionConstructor = Helpers.GetConstructor(() => new LossyNumericConversionException());

            return new Conversion(
                w =>
                {
                    w.IL.Emit(Dup); // stack is [from, from]
                    using var originalValue = w.UseLocal(from);
                    w.IL.Emit(Stloc, originalValue); // stack is [from]
                    w.IL.Emit(Call, convertMethod); // stack is [to]
                    w.IL.Emit(Dup); // stack is [to, to]
                    w.IL.Emit(Call, reverseConvertMethod); // stack is [to, backToFrom]
                    w.IL.Emit(Ldloc, originalValue); // stack is [to, backToFrom, from]
                    var successLabel = w.IL.DefineLabel();
                    if (equalsOperator != null)
                    {
                        w.IL.Emit(Call, equalsOperator); // stack is [to]
                        w.IL.Emit(Brtrue, successLabel);
                    }
                    else
                    {
                        w.IL.Emit(Beq, successLabel); // stack is [to]
                    }
                    w.IL.Emit(Newobj, exceptionConstructor);
                    w.IL.Emit(Throw);
                    w.IL.MarkLabel(successLabel);
                },
                IsSafe: false
            );
        }

        Conversion GetIntegralToIntegralConversion()
        {
            var isSafe = fromNumericTypeFacts.Size < toNumericTypeFacts.Size
                && fromNumericTypeFacts.IsUnsigned == toNumericTypeFacts.IsUnsigned;
            var opCodeName = new StringBuilder("conv.");
            if (!isSafe) { opCodeName.Append("ovf."); }
            opCodeName.Append(toNumericTypeFacts.IsUnsigned ? 'u' : 'i')
                .Append(toNumericTypeFacts.Size);
            if (!isSafe && fromNumericTypeFacts.IsUnsigned) { opCodeName.Append(".un"); }
            var opCode = OpCodes[opCodeName.ToString()];

            return new Conversion(w => w.IL.Emit(opCode), IsSafe: isSafe);
        }

        Conversion GetSafeConversionToFloatingPoint()
        {
            // 4 possible paths:
            // unsigned -> float
            // signed -> float
            // unsigned -> float -> double
            // signed -> double

            return new Conversion(
                w =>
                {
                    if (fromNumericTypeFacts.IsUnsigned) { w.IL.Emit(Conv_R_Un); }
                    else if (toNumericTypeFacts.Size == 4) { w.IL.Emit(Conv_R4); }

                    if (toNumericTypeFacts.Size == 8) { w.IL.Emit(Conv_R8); }
                },
                IsSafe: true
            );
        }
    }

    private static Conversion? GetImplicitConversionOrDefault(Type from, Type to)
    {
        var conversionMethod = GetImplicitConversionOrDefault(to)
            ?? GetImplicitConversionOrDefault(from);
        if (conversionMethod == null) { return null; }

        return new Conversion(
            w => w.IL.Emit(Call, conversionMethod),
            // Enumerate some well-known implicit conversions we can trust not to throw. Arbitrary
            // user-defined conversions could throw.
            IsSafe: conversionMethod.DeclaringType == typeof(decimal)
                || (conversionMethod.DeclaringType == typeof(DateTimeOffset) && from == typeof(DateTime))
        );

        MethodInfo? GetImplicitConversionOrDefault(Type source) =>
            source.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(
                    m => m.Name == "op_Implicit"
                        && m.ReturnType == to
                        && m.GetParameters().Select(p => p.ParameterType).SequenceEqual(new[] { from })
                );
    }

    public override bool Equals(object? obj) => obj is ScalarConverter that && this.EqualsNonNullable(that);

    public bool Equals(ScalarConverter? that) => that != null && this.EqualsNonNullable(that);

    private bool EqualsNonNullable(ScalarConverter that) => true; // todo

    public override int GetHashCode() => 0;

    public static MethodInfo? GetEqualityOperatorOrDefault(Type type) =>
        type.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .FirstOrDefault(
                m => m.Name == "op_Equality"
                    && m.ReturnType == typeof(bool)
                    && m.GetParameters().Select(p => p.ParameterType).SequenceEqual(new[] { type, type })
            );

    // todo should contain ScalarConverter
    private readonly record struct CacheKey(Type From, Type To);

    internal readonly record struct NumericTypeFacts(int Size, bool IsFloatingPoint = false, bool IsUnsigned = false);

    public record Conversion(Action<ILWriter> WriteConversion, bool IsSafe);
}
