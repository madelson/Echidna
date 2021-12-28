using Medallion.Data.Mapping;
using System.Collections.Concurrent;
using System.Reflection;
using System.Reflection.Emit;

namespace Medallion.Data.Tests.Mapping;

internal class ScalarConverterTest
{
    private static readonly object Error = new(), NoConversion = new();

    private static readonly ConcurrentDictionary<(Type From, Type To), (Delegate? Convert, bool IsSafe)> ConversionsCache = new();

    [Test]
    public void TestCanConvertBetweenNumericTypes()
    {
        var numericTypes = ScalarConverter.SimpleNumericTypes.Keys
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
        var numericTypes = ScalarConverter.SimpleNumericTypes.Keys
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
        throw new NotImplementedException();
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
}
