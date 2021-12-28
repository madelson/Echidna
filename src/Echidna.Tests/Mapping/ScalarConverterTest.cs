using Medallion.Data.Mapping;
using System.Collections.Concurrent;
using System.Reflection;
using System.Reflection.Emit;
using System.Text.RegularExpressions;

namespace Medallion.Data.Tests.Mapping;

internal class ScalarConverterTest
{
    private static readonly object Error = new(), NoConversion = new();

    private static readonly ConcurrentDictionary<(Type From, Type To), (Delegate? Convert, bool IsSafe)> ConversionsCache = new();

    [Test]
    public void TestCanConvertBetweenNumericTypes()
    {
        var numericTypes = NumericTypeFacts.SimpleNumericTypes
            .OrderBy(t => t.Name)
            .ToArray();

        foreach (var from in numericTypes)
        {
            var fromValues = new List<object>
            {
                Activator.CreateInstance(from)!, // 0
                from.GetField("MinValue")!.GetValue(null)!,
                from.GetField("MaxValue")!.GetValue(null)!,
            };
            if (from == typeof(float) || from == typeof(double))
            {
                fromValues.Add(Convert.ChangeType("∞", from));
                fromValues.Add(Convert.ChangeType("-∞", from));
            }
            if (from == typeof(float) || from == typeof(double) || from == typeof(decimal))
            {
                fromValues.Add(Convert.ChangeType("12.7", from));
            }
            else { fromValues.Add(Convert.ChangeType("123", from)); }

            foreach (var to in numericTypes.Where(t => t != from))
            {
                foreach (var fromValue in fromValues)
                {
                    object expected;
                    try 
                    { 
                        expected = Convert.ChangeType(fromValue, to);
                        var reversed = Convert.ChangeType(expected, from);
                        if (!Equals(reversed, fromValue)) { expected = Error; }
                    }
                    catch { expected = Error; }

                    TestCanConvert(fromValue, to, expected);
                }
            }
        }
    }

    [Test]
    public void TestCanConvertBetweenNumericAndBooleanTypes()
    {
        var numericTypes = NumericTypeFacts.SimpleNumericTypes
            .OrderBy(t => t.Name)
            .ToArray();
        
        foreach (var numericType in numericTypes)
        {
            var zero = Activator.CreateInstance(numericType)!;
            var one = numericType.GetMethod("Parse", BindingFlags.Public | BindingFlags.Static, new[] { typeof(string) })!
                .Invoke(null, new[] { "1" })!;
            var maxValue = numericType.GetField("MaxValue")!.GetValue(null)!;

            TestCanConvert(false, numericType, zero);
            TestCanConvert(true, numericType, one);

            TestCanConvert(zero, typeof(bool), false);
            TestCanConvert(one, typeof(bool), true);
            TestCanConvert(maxValue, typeof(bool), Error);
        }
    }

    [Test]
    public void TestCanConvertUnspecifiedDateTimeToDateOnly()
    {
        var date = DateTimeOffset.Parse("2021-12-27T12:01:45.8267207-05:00");

        TestCanConvert(date, typeof(DateOnly), NoConversion); // can't convert from DateTimeOffset
        TestCanConvert(date.DateTime, typeof(DateOnly), Error); // has time
        TestCanConvert(date.Date, typeof(DateOnly), new DateOnly(2021, 12, 27));
        TestCanConvert(DateTime.SpecifyKind(date.Date, DateTimeKind.Utc), typeof(DateOnly), Error); // has zone
        TestCanConvert(DateTime.SpecifyKind(date.Date, DateTimeKind.Local), typeof(DateOnly), Error); // has zone
    }

    [Test]
    public void TestCanConvertTimeSpanToTimeOnly()
    {
        TestCanConvert(TimeSpan.Zero, typeof(TimeOnly), TimeOnly.MinValue);
        TestCanConvert(TimeOnly.MaxValue.ToTimeSpan(), typeof(TimeOnly), TimeOnly.MaxValue);
        TestCanConvert(TimeSpan.FromTicks(TimeOnly.MinValue.Ticks - 1), typeof(TimeOnly), Error); // out of range
        TestCanConvert(TimeSpan.FromTicks(TimeOnly.MaxValue.Ticks + 1), typeof(TimeOnly), Error); // out of range
    }

    [Test]
    public void TestCanConvertNumericValueToEnum()
    {
        // enum with adjacent values
        TestCanConvert((decimal)DateTimeKind.Utc, typeof(DateTimeKind), DateTimeKind.Utc);
        TestCanConvert((byte)DateTimeKind.Utc, typeof(DateTimeKind), DateTimeKind.Utc);
        TestCanConvert(-1, typeof(DateTimeKind), Error);
        TestCanConvert(10.0, typeof(DateTimeKind), Error);

        // flags enum with adjacent flags
        TestCanConvert((float)(RegexOptions.IgnoreCase | RegexOptions.Multiline), typeof(RegexOptions), RegexOptions.IgnoreCase | RegexOptions.Multiline);
        TestCanConvert((short)(RegexOptions.RightToLeft | RegexOptions.Compiled | RegexOptions.CultureInvariant), typeof(RegexOptions), RegexOptions.RightToLeft | RegexOptions.Compiled | RegexOptions.CultureInvariant);
        TestCanConvert(-1, typeof(RegexOptions), Error);
        TestCanConvert(1U << 31, typeof(RegexOptions), Error);

        // enum with non-adjacent values
        foreach (var value in Enum.GetValues(typeof(EnumWithNonAdjacentValues)).Cast<EnumWithNonAdjacentValues>())
        {
            TestCanConvert((long)value, typeof(EnumWithNonAdjacentValues), value);
        }
        TestCanConvert(0, typeof(EnumWithNonAdjacentValues), Error);
        TestCanConvert(4L, typeof(EnumWithNonAdjacentValues), Error);

        // enum with non-adjacent flags
        foreach (var value in Enum.GetValues(typeof(EnumWithNonAdjacentFlags)).Cast<EnumWithNonAdjacentFlags>())
        {
            TestCanConvert((long)value, typeof(EnumWithNonAdjacentFlags), value);
        }
        TestCanConvert(0, typeof(EnumWithNonAdjacentFlags), default(EnumWithNonAdjacentFlags));
        TestCanConvert(1 << 1, typeof(EnumWithNonAdjacentFlags), Error);
        TestCanConvert(1 << 6, typeof(EnumWithNonAdjacentFlags), Error);

        // empty enums
        TestCanConvert(0, typeof(EmptyEnum), Error);
        TestCanConvert(0, typeof(EmptyFlagsEnum), default(EmptyFlagsEnum));
        TestCanConvert(1, typeof(EmptyFlagsEnum), Error);
    }

    private static void TestCanConvert(object fromValue, Type to, object expected)
    {
        var (convert, isSafe) = ConversionsCache.GetOrAdd(
            (fromValue.GetType(), to),
            static key =>
            {
                if (new ScalarConverter().CanConvert(key.From, key.To, out var conversion))
                {
                    var dynamicMethod = new DynamicMethod(
                        $"Convert_{key.From.Name}_{key.To.Name}",
                        key.To,
                        new[] { key.From },
                        typeof(ScalarConverter).Module,
                        skipVisibility: true
                    );
                    var ilWriter = new ILWriter(dynamicMethod.GetILGenerator());
                    ilWriter.IL.Emit(OpCodes.Ldarg_0);
                    conversion.WriteConversion(ilWriter);
                    ilWriter.IL.Emit(OpCodes.Ret);
                    return (dynamicMethod.CreateDelegate(typeof(Func<,>).MakeGenericType(key.From, key.To)), conversion.IsSafe);
                }
                else
                {
                    return (null, false);
                }
            });

        const string Message = "{0} ({1}) -> {2}";
        var args = new object?[] { fromValue, fromValue.GetType(), to };

        if (expected == NoConversion) 
        { 
            Assert.IsNull(convert, Message, args);
            return;
        }
        
        Assert.IsNotNull(convert, Message, args);

        if (expected == Error)
        {
            Assert.Catch(() => convert!.DynamicInvoke(fromValue), Message, args);
        }
        else
        {
            object? result = null;
            Assert.DoesNotThrow(() => result = convert!.DynamicInvoke(fromValue), Message, args);
            Assert.AreEqual(expected, result, Message, args);
        }
    }

    internal enum EnumWithNonAdjacentValues : long
    {
        A = long.MinValue,
        B = -100,
        C = 1,
        D = 2,
        E = 3,
        F = 999,
    }

    [Flags]
    internal enum EnumWithNonAdjacentFlags : ushort
    {
        A = 1 << 3,
        B = 1 << 5,
        C = 1 << 7,
        D = B | C,
    }

    internal enum EmptyEnum : byte { }

    [Flags]
    internal enum EmptyFlagsEnum : sbyte { }
}
