using Medallion.Data.Entities;
using System.Linq.Expressions;

namespace Medallion.Data.Tests.Entities;

internal class ExpressionEqualityVisitorTest
{
    [Test]
    public void TestCanDetectEqualExpressions()
    {
        AssertEquality(Create(1), Create(1), equal: true);
        AssertEquality(Create(2), Create(3), equal: true); // differ only by closure field value
        AssertEquality(Create(10), Create2(10), equal: true); // differ only by closure type
        AssertEquality(Create(5), Create2(-5), equal: true); // differ only by closure field value and closure type
    }

    [Test]
    public void TestCanDetectNotEqualExpressions()
    {
        var k = 2;
        Expression<Func<int>> e = () => 2 * (k + 2);
        ExpressionEqualityVisitor visitor = new();
        AssertEquality(e, Create(2), equal: false);
    }

    private Expression Create(int i)
    {
        Expression<Func<int>> e = () => 2 * (i + 1);
        return e;
    }

    private Expression Create2(int j)
    {
        Expression<Func<int>> e = () => 2 * (j + 1);
        return e;
    }

    private static void AssertEquality(Expression? @this, Expression? that, bool equal)
    {
        ExpressionEqualityVisitor equalityVisitor = new();
        var areEqual = equalityVisitor.Equals(@this, that);
        Assert.AreEqual(equal, areEqual);
        ExpressionHashingVisitor hashingVisitor = new();
        var thisHash = hashingVisitor.GetHashCode(@this);
        var thatHash = hashingVisitor.GetHashCode(that);
        if (equal)
        {
            Assert.AreEqual(thatHash, thisHash);
        }
        else
        {
            Assert.AreNotEqual(thatHash, thisHash);
        }
    }
}
