using System.Linq.Expressions;
using System.Reflection;

namespace Medallion.Data;

internal static class Helpers
{
    public static T As<T>(this T @this) => @this;

    // TODO revisit these expressions references

    public static MethodInfo GetMethod(Expression<Action> expression) =>
        (MethodInfo)GetMethod(expression.As<LambdaExpression>());

    public static MethodInfo GetMethod<TResult>(Expression<Func<TResult>> expression) =>
        (MethodInfo)GetMethod(expression.As<LambdaExpression>());

    public static ConstructorInfo GetConstructor<TNew>(Expression<Func<TNew>> expression) =>
        (ConstructorInfo)GetMethod(expression.As<LambdaExpression>());

    private static MethodBase GetMethod(LambdaExpression expression) =>
        expression.Body switch
        {
            MethodCallExpression call => call.Method,
            BinaryExpression binary => binary.Method ?? throw Invariant.ShouldNeverGetHere(),
            NewExpression @new => @new.Constructor ?? throw Invariant.ShouldNeverGetHere(),
            _ => throw Invariant.ShouldNeverGetHere()
        };
}
