using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Medallion.Data.Mapping;

internal readonly record struct NumericTypeFacts(int Size, bool IsFloatingPoint = false, bool IsUnsigned = false)
{
    private static readonly Dictionary<Type, NumericTypeFacts> SimpleNumericTypeFacts = new Dictionary<Type, NumericTypeFacts>()
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

    public static IReadOnlyCollection<Type> SimpleNumericTypes => SimpleNumericTypeFacts.Keys;

    public static bool TryGetFor(Type type, out NumericTypeFacts value) => SimpleNumericTypeFacts.TryGetValue(type, out value);

    public static NumericTypeFacts For(Type type) => SimpleNumericTypeFacts[type];
}
