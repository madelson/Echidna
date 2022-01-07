namespace Medallion.Data;

internal static class Helpers
{
    public static T As<T>(this T @this) => @this;

    public static bool CanBeNull(this Type type) => !type.IsValueType || Nullable.GetUnderlyingType(type) != null;
}
