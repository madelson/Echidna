using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Medallion.Data.Mapping;

internal static class Nullability
{
    // copied from NullabilityInfoContext.cs
    internal static bool IsSupported { get; } =
        AppContext.TryGetSwitch("System.Reflection.NullabilityInfoContext.IsSupported", out bool isSupported) ? isSupported : true;

    private static readonly NullabilityInfoContext Context = new();

    public static bool TryGetFor(ParameterInfo parameter, [NotNullWhen(returnValue: true)] out NullabilityInfo? nullabilityInfo)
    {
        if (!IsSupported)
        {
            nullabilityInfo = null;
            return false;
        }

        lock (Context) { nullabilityInfo = Context.Create(parameter); }
        return true;
    }

    public static bool TryGetFor(PropertyInfo property, [NotNullWhen(returnValue: true)] out NullabilityInfo? nullabilityInfo)
    {
        if (!IsSupported)
        {
            nullabilityInfo = null;
            return false;
        }

        lock (Context) { nullabilityInfo = Context.Create(property); }
        return true;
    }
}
