using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;

namespace Medallion.Data.Entities;

/// <summary>
/// Visitor that transforms a <see cref="LambdaExpression"/> by "hoisting" out
/// sub-expressions that do not reference any of the input parameters. These are replaced
/// by invocations of compiled delegates
/// </summary>
internal class ExpressionHoistingVisitor : ExpressionVisitor
{
    private readonly List<ParameterExpression> _parameters = new();
    private readonly Stack<Expression?> _hoistableChildren = new();
    private readonly Stack<Expression> _hoistableExpressions = new();
    private readonly ExpressionReplacingVisitor _replacingVisitor = new();

    public TLambdaExpression Hoist<TLambdaExpression>(TLambdaExpression expression)
        where TLambdaExpression : LambdaExpression
    {
        this.Reset();
        this._parameters.AddRange(expression.Parameters);

        var result = HoistHelper();

        this.Reset();
        return result;

        TLambdaExpression HoistHelper()
        {
            this.Visit(expression);
            // the lambda itself is never hoistable, so we should always end up with one null for hoistable children
            Debug.Assert(this._hoistableChildren.Count == 1 && this._hoistableChildren.Peek() is null);

            if (this._hoistableExpressions.Count == 0)
            {
                return expression; // nothing to hoist
            }

            foreach (var hoistableExpression in this._hoistableExpressions)
            {
                var replacementExpresion = this.CreateReplacementExpression(hoistableExpression);
                // TryAdd() because expressions can appear multiple times in the same tree so we might
                // have duplicate hoistable expressions
                this._replacingVisitor.Replacements.TryAdd(hoistableExpression, replacementExpresion);
            }
            return this._replacingVisitor.VisitAndConvert(expression, nameof(this.Hoist));
        }
    }

    public static bool IsHoistedExpression(Expression expression, [NotNullWhen(true)] out Delegate? @delegate)
    {
        if (expression is InvocationExpression invocation
            && invocation.Arguments.Count == 0
            && invocation.Expression is ConstantExpression constant
            && constant.Value is { } value)
        {
            @delegate = (Delegate)value;
            return true;
        }

        @delegate = null;
        return false;
    }

    private void Reset()
    {
        this._parameters.Clear();
        this._hoistableChildren.Clear();
        this._hoistableExpressions.Clear();
        this._replacingVisitor.Replacements.Clear();
    }

    private Expression CreateReplacementExpression(Expression hoistedExpression)
    {
        var @delegate = Expression.Lambda(hoistedExpression).Compile();
        return Expression.Invoke(Expression.Constant(@delegate, @delegate.GetType()));
    }

    [return: NotNullIfNotNull("node")]
    public override Expression? Visit(Expression? node)
    {
        // Nulls and constants shouldn't be hoisted nor should they prevent
        // their parents from being hoisted. Therefore, we just noop.
        if (node is null || node.NodeType == ExpressionType.Constant) 
        {
            return node; 
        }

        if (node is ParameterExpression parameter)
        {
            this._hoistableChildren.Push(this._parameters.Contains(parameter) ? null : node);
            return node;
        }

        var originalHoistableChildrenCount = this._hoistableChildren.Count;
        base.Visit(node);

        var originalHoistableExpressionsCount = this._hoistableExpressions.Count;
        var canHoist = true;
        while (this._hoistableChildren.Count > originalHoistableChildrenCount)
        {
            if (this._hoistableChildren.Pop() is { } hoistableChild)
            {
                this._hoistableExpressions.Push(hoistableChild);
            }
            else
            {
                canHoist = false;
            }
        }

        if (canHoist)
        {
            // If this node is hoistable, then remove all children from the set of hoistable expressions
            // and instead add this node as a hoistable child
            while (this._hoistableExpressions.Count > originalHoistableExpressionsCount)
            {
                this._hoistableExpressions.Pop();
            }
            this._hoistableChildren.Push(node);
        }
        else
        {
            this._hoistableChildren.Push(null);
        }

        return node;
    }

    private sealed class ExpressionReplacingVisitor : ExpressionVisitor
    {
        public Dictionary<Expression, Expression> Replacements { get; } = new();

        [return: NotNullIfNotNull("node")]
        public override Expression? Visit(Expression? node) =>
            node is null ? null
                : this.Replacements.TryGetValue(node, out var replacement) ? replacement
                : base.Visit(node);
    }
}
