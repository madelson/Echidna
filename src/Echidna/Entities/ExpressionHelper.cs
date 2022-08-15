using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Medallion.Data.Entities;

internal static class ExpressionHelper
{
    private static readonly MicroCache<Type, bool> IsClosureLikeCache = new(maxCount: 5000);

    public static bool IsClosureAccess(MemberExpression expression) =>
        expression.Expression is ConstantExpression constant
            && constant.Value is { } value
            && IsClosureLike(value.GetType());

    private static bool IsClosureLike(Type type)
    {
        // short-cut checks
        if (type.IsPrimitive || type.IsPublic || type.IsAbstract || type.IsValueType) { return false; }

        return IsClosureLikeCache.GetOrAdd(type, static t => t.GetCustomAttribute<CompilerGeneratedAttribute>() != null);
    }
}
