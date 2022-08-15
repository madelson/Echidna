using System.Linq.Expressions;

namespace Medallion.Data.Entities;

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
