using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using static System.Reflection.Emit.OpCodes;

namespace Medallion.Data.Mapping;

// TODO string -> char
// TODO string -> enum (name only, not numbers). Should use string.Equals(value, constant, StringComparison.OrdinalIgnoreCase)

internal class ScalarConverter : IEquatable<ScalarConverter>
{
    private static readonly MicroCache<CacheKey, Conversion?> Cache = new(maxCount: 5000);

    // TODO lazy?
    private static readonly IReadOnlyDictionary<string, OpCode> OpCodes = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(f => f.FieldType == typeof(OpCode))
        .Select(f => (OpCode)f.GetValue(null)!)
        .ToDictionary(c => c.Name!);

    public bool CanConvert(
        Type from,
        Type to,
        [NotNullWhen(returnValue: true)] out Conversion? conversion)
    {
        Invariant.Require(from != to, "should not be called for trivial conversions");

        conversion = Cache.GetOrAdd(
            new(from, to),
            static (key, converter) => converter.GetConversionOrDefault(key.From, key.To),
            this
        );
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
 
        if (NumericTypeFacts.TryGetFor(from, out var fromNumericTypeFacts))
        {
            // numeric -> numeric
            if (NumericTypeFacts.TryGetFor(to, out var toNumericTypeFacts))
            {
                return GetNumericToNumericConversion(from, fromNumericTypeFacts, to, toNumericTypeFacts);
            }

            // numeric -> enum
            if (to.IsEnum)
            {
                return GetNumericToEnumConversion(from, fromNumericTypeFacts, to);
            }

            // numeric -> boolean
            if (to == typeof(bool))
            {
                return GetNumericToBooleanConversion(from, fromNumericTypeFacts);
            }
        }

        // boolean -> numeric
        if (from == typeof(bool) && NumericTypeFacts.SimpleNumericTypes.Contains(to))
        {
            var convertMethod = MappingMethods.ConvertMethods[(from, to)];
            return new(w => w.IL.Emit(Call, convertMethod), IsSafe: true);
        }

        if (from == typeof(DateTime) && to == typeof(DateOnly))
        {
            var convertMethod = MappingMethods.DateTimeToDateOnly;
            return new(w => w.IL.Emit(Call, convertMethod), IsSafe: false);
        }

        if (from == typeof(TimeSpan) && to == typeof(TimeOnly))
        {
            var convertMethod = MappingMethods.TimeSpanToTimeOnly;
            return new(w => w.IL.Emit(Call, convertMethod), IsSafe: false);
        }

        return GetImplicitConversionOrDefault(from, to);
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
        else if (!this.CanConvert(from, toNullableUnderlyingType, out conversionToNullableUnderlyingType))
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

    private static Conversion GetNumericToNumericConversion(
        Type from,
        NumericTypeFacts fromNumericTypeFacts,
        Type to,
        NumericTypeFacts toNumericTypeFacts)
    {
        // integral->integral conversions can take advantage of native int conversion opcodes
        if (from.IsPrimitive && to.IsPrimitive && !fromNumericTypeFacts.IsFloatingPoint && !toNumericTypeFacts.IsFloatingPoint)
        {
            return GetIntegralToIntegralConversion();
        }

        // safe integral/floating point->floating point conversions can use native float conversion opcodes
        if (toNumericTypeFacts.IsFloatingPoint && toNumericTypeFacts.Size > fromNumericTypeFacts.Size)
        {
            return GetSafeConversionToFloatingPoint();
        }

        // integral->decimal can just use decimal's implicit conversions
        if (from.IsPrimitive && !fromNumericTypeFacts.IsFloatingPoint && to == typeof(decimal))
        {
            return GetImplicitConversionOrDefault(from, to)
                ?? throw Invariant.ShouldNeverGetHere();
        }

        // For other methods, leverage Convert functions with a round-trip check to make sure the
        // conversion was not lossy
        return GetConversionWithRoundTripCheck();

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

            return new(w => w.IL.Emit(opCode), IsSafe: isSafe);
        }

        Conversion GetSafeConversionToFloatingPoint()
        {
            // 4 possible paths:
            // unsigned -> float
            // signed -> float
            // unsigned -> float -> double
            // signed -> double

            return new(
                w =>
                {
                    if (fromNumericTypeFacts.IsUnsigned) { w.IL.Emit(Conv_R_Un); }
                    else if (toNumericTypeFacts.Size == 4) { w.IL.Emit(Conv_R4); }

                    if (toNumericTypeFacts.Size == 8) { w.IL.Emit(Conv_R8); }
                },
                IsSafe: true
            );
        }

        Conversion GetConversionWithRoundTripCheck()
        {
            // Generates code like the following:
            // var converted = Convert.ToTo(from);
            // var convertedBack = Convert.ToFrom(converted);
            // if (!(convertedBack == converted)) { throw ... }

            var convertMethod = MappingMethods.ConvertMethods[(from, to)];
            var equalsOperator = from == typeof(decimal)
                ? GetEqualityOperatorOrDefault(typeof(decimal))
                : null;
            var reverseConvertMethod = MappingMethods.ConvertMethods[(to, from)];
            
            return new(
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
                    w.IL.Emit(Newobj, MappingMethods.LossyConversionExceptionConstructor);
                    w.IL.Emit(Throw);
                    w.IL.MarkLabel(successLabel);
                },
                IsSafe: false
            );
        }        
    }

    private static Conversion GetNumericToEnumConversion(Type from, NumericTypeFacts fromNumericTypeFacts, Type to)
    {
        // TODO move this into EnumValidationHelper
        var underlyingType = Enum.GetUnderlyingType(to);
        NumericTypeFacts underlyingNumericTypeFacts;
        Conversion? conversionToUnderlyingType;
        if (from == underlyingType)
        {
            underlyingNumericTypeFacts = fromNumericTypeFacts;
            conversionToUnderlyingType = null;
        }
        else
        {
            underlyingNumericTypeFacts = NumericTypeFacts.For(underlyingType);
            conversionToUnderlyingType = GetNumericToNumericConversion(from, fromNumericTypeFacts, underlyingType, underlyingNumericTypeFacts);
        }

        var (definedRanges, definedFlags) = EnumValidationHelper.GetDefinedValues(to);

        void LoadEnumConstant(ILWriter writer, object value)
        {
            if (underlyingNumericTypeFacts.Size == 8)
            {
                writer.IL.Emit(Ldc_I8, underlyingNumericTypeFacts.IsUnsigned ? unchecked((long)(ulong)value) : (long)value);
            }
            else
            {
                writer.IL.Emit(Ldc_I4, underlyingNumericTypeFacts.IsUnsigned ? unchecked((int)Convert.ToUInt32(value)) : Convert.ToInt32(value));
            }
        }

        return new(
            w =>
            {
                conversionToUnderlyingType?.WriteConversion(w); // stack is [convertedFrom]

                var successLabel = w.IL.DefineLabel();
                var failureLabel = w.IL.DefineLabel();

                if (definedRanges != null)
                {
                    foreach (var (start, end) in definedRanges)
                    {
                        if (Equals(start, end)) // single-value range => just check for equality
                        {
                            w.IL.Emit(Dup); // stack is [convertedFrom, convertedFrom]
                            LoadEnumConstant(w, start); // stack is [convertedFrom, convertedFrom, start]
                            w.IL.Emit(Beq, successLabel); // stack is [convertedFrom]
                        }
                        else // multi-value range => check for inclusion
                        {
                            w.IL.Emit(Dup); // stack is [convertedFrom, convertedFrom]
                            LoadEnumConstant(w, start); // stack is [convertedFrom, convertedFrom, start]
                            w.IL.Emit(underlyingNumericTypeFacts.IsUnsigned ? Blt_Un : Blt, failureLabel); // stack is [convertedFrom]
                            w.IL.Emit(Dup); // stack is [convertedFrom, convertedFrom]
                            LoadEnumConstant(w, end); // stack is [convertedFrom, convertedFrom, end]
                            w.IL.Emit(underlyingNumericTypeFacts.IsUnsigned ? Ble_Un : Ble, successLabel); // stack is [convertedFrom]
                        }
                    }
                }
                else
                {
                    Invariant.Require(definedFlags != null);

                    w.IL.Emit(Dup); // stack is [convertedFrom, convertedFrom]
                    LoadEnumConstant(w, definedFlags!); // stack is [convertedFrom, convertedFrom, flags]
                    w.IL.Emit(Not); // stack is [convertedFrom, convertedFrom, ~flags]
                    w.IL.Emit(And); // stack is [convertedFrom, convertedFrom & ~flags]
                    w.IL.Emit(Brfalse, successLabel); // stack is [convertedFrom]
                }

                w.IL.MarkLabel(failureLabel);
                w.IL.Emit(Newobj, MappingMethods.ArgumentOutOfRangeExceptionConstructor);
                w.IL.Emit(Throw);
                w.IL.MarkLabel(successLabel);
            },
            IsSafe: false
        );
    }

    private static Conversion GetNumericToBooleanConversion(Type from, NumericTypeFacts fromNumericTypeFacts)
    {
        var conversionToInt = from == typeof(int)
            ? null
            : GetNumericToNumericConversion(from, fromNumericTypeFacts, typeof(int), NumericTypeFacts.For(typeof(int)));
        return new(
            w =>
            {
                conversionToInt?.WriteConversion(w); // stack contains [fromAsInt]
                w.IL.Emit(Dup); // stack contains [fromAsInt, fromAsInt]
                w.IL.Emit(Ldc_I4, ~1); // stack contains [fromAsInt, fromAsInt, ~1]
                w.IL.Emit(And); // stack contains [fromAsInt, fromAsInt & ~1]
                var successLabel = w.IL.DefineLabel();
                w.IL.Emit(Brfalse, successLabel); // stack contains [fromAsInt]
                w.IL.Emit(Newobj, MappingMethods.LossyConversionExceptionConstructor);
                w.IL.Emit(Throw);
                w.IL.MarkLabel(successLabel);
            },
            IsSafe: false
        );
    }

    private static Conversion? GetImplicitConversionOrDefault(Type from, Type to)
    {
        var conversionMethod = GetImplicitConversionOrDefault(to)
            ?? GetImplicitConversionOrDefault(from);
        if (conversionMethod == null) { return null; }

        return new(
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

    // TODO move to mapping methods
    private static MethodInfo? GetEqualityOperatorOrDefault(Type type) =>
        type.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .FirstOrDefault(
                m => m.Name == "op_Equality"
                    && m.ReturnType == typeof(bool)
                    && m.GetParameters().Select(p => p.ParameterType).SequenceEqual(new[] { type, type })
            );

    public override bool Equals(object? obj) => obj is ScalarConverter that && this.EqualsNonNullable(that);

    public bool Equals(ScalarConverter? that) => that != null && this.EqualsNonNullable(that);

    private bool EqualsNonNullable(ScalarConverter that) => true; // todo

    public override int GetHashCode() => 0;

    // todo should contain ScalarConverter
    private readonly record struct CacheKey(Type From, Type To);

    public record Conversion(Action<ILWriter> WriteConversion, bool IsSafe);
}
