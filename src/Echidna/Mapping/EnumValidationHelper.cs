using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Medallion.Data.Mapping;

internal static class EnumValidationHelper
{
    // TODO this can be even better.
    // For any unsigned enum with valid values 0..N, we can just check <= N
    // For any signed enum with valid values 0..N we can just check ((unsigned)value) <= N
    // Basically there should be a new option to return "DefinedUnsignedMaxValue"

    /// <summary>
    /// For a non-flags enum, returns a sorted list of contiguous value ranges for the defined enum values.
    /// 
    /// For a flags enum, returns a single value that is the bitwise OR of all defined enum values (or 0 if no values are defined).
    /// </summary>
    public static (List<(object Start, object End)>? DefinedRanges, object? DefinedFlags) GetDefinedValues(Type type)
    {
        var definedValues = Enum.GetValues(type);

        if (type.GetCustomAttribute<FlagsAttribute>() == null)
        {
            var ranges = new List<(object Start, object End)>();
            if (definedValues.Length > 0)
            {
                Array.Sort(definedValues);

                object start, end;
                start = end = definedValues.GetValue(0)!;
                for (var i = 1; i < definedValues.Length; i++)
                {
                    var value = definedValues.GetValue(i)!;
                    if (Convert.ToDecimal(value) - Convert.ToDecimal(end) <= 1M)
                    {
                        end = value; // extend the range
                    }
                    else // new range
                    {
                        ranges.Add((start, end));
                        start = end = value;
                    }
                }
                ranges.Add((start, end));
            }
            return (ranges, null);
        }

        
        // flags enum with no flags: only 0 is valid
        if (definedValues.Length == 0)
        {
            return (null, Activator.CreateInstance(type)!);
        }

        object definedFlags;
        var underlyingType = Enum.GetUnderlyingType(type);
        if (NumericTypeFacts.For(underlyingType).IsUnsigned)
        {
            var definedFlagsUInt64 = 0UL;
            foreach (var value in definedValues)
            {
                definedFlagsUInt64 |= Convert.ToUInt64(value);
            }
            definedFlags = definedFlagsUInt64;
        }
        else
        {
            var definedFlagsInt64 = 0L;
            foreach (var value in definedValues)
            {
                definedFlagsInt64 |= Convert.ToInt64(value);
            }
            definedFlags = definedFlagsInt64;
        }
        return (null, Enum.ToObject(type, Convert.ChangeType(definedFlags, underlyingType)));
    }
}