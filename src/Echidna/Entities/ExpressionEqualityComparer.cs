using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;

namespace Medallion.Data.Entities;

/// <summary>
/// <see cref="IEqualityComparer{T}"/> based on <see cref="ExpressionEqualityVisitor"/>
/// and <see cref="ExpressionHashingVisitor"/>
/// </summary>
internal sealed class ExpressionEqualityComparer : IEqualityComparer<Expression>
{
    public static ExpressionEqualityComparer Instance { get; } = new();

    private ExpressionEqualityComparer() { }

    public bool Equals(Expression? x, Expression? y) => 
        ReferenceEquals(x, y) || Visitors.Current.EqualityVisitor.Equals(x, y);

    public int GetHashCode([DisallowNull] Expression obj) => Visitors.Current.HashingVisitor.GetHashCode(obj);

    private sealed class Visitors
    {
        // We cache visitors by thread because visitor instances are not thread-safe
        [ThreadStatic]
        private static Visitors? _current;

        public static Visitors Current => _current ??= new();

        internal readonly ExpressionHashingVisitor HashingVisitor = new();
        internal readonly ExpressionEqualityVisitor EqualityVisitor = new();
    }
}
