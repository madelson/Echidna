using System.Linq.Expressions;

namespace Medallion.Data.Entities;

/// <summary>
/// Determines whether 2 expressions are equal modulo accessing closures
/// </summary>
internal partial class ExpressionEqualityVisitor
{
    private object? _other;
    private bool _failed;

    public bool Equals(Expression? @this, Expression? that)
    {
        this._other = that;
        this._failed = false;
        this.Visit(@this);
        return !this._failed;
    }
}
