using Medallion.Data.Mapping;
using System.Collections.Concurrent;
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
    public void DebugTest()
    {
        TestCanConvert((decimal)0, typeof(double), 0.0);
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
