using System.Data.SqlTypes;

namespace Medallion.Data.Mapping;

internal static class MappingHelpers
{
    public static DateOnly ToDateOnly(DateTime dateTime)
    {
        if (dateTime.TimeOfDay != TimeSpan.Zero) { ThrowBadTimeOfDay(); }
        if (dateTime.Kind != DateTimeKind.Unspecified) { ThrowBadKind(dateTime); }

        return DateOnly.FromDateTime(dateTime);

        static void ThrowBadTimeOfDay() =>
            throw new LossyConversionException($"{typeof(DateTime)} can only be converted to {typeof(DateOnly)} if it has a {nameof(DateTime.TimeOfDay)} value of {TimeSpan.Zero}");
        static void ThrowBadKind(DateTime dateTime) =>
            throw new LossyConversionException($"{typeof(DateTime)} can only be converted to {typeof(DateOnly)} it if has a {nameof(DateTime.Kind)} value of {DateTimeKind.Unspecified}. Found {dateTime.Kind}.");
    }
}
