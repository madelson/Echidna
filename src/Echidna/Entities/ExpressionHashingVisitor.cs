using System.Linq.Expressions;

namespace Medallion.Data.Entities;

/// <summary>
/// Implements hashing for expressions that is consistent with <see cref="ExpressionEqualityVisitor"/>
/// </summary>
internal partial class ExpressionHashingVisitor
{
    private HashCode _hash;

    public int GetHashCode(Expression? expression)
    {
        this._hash = default;
        this.Visit(expression);
        return this._hash.ToHashCode();
    }
}
