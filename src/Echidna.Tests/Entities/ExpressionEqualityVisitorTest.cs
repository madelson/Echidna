using Medallion.Data.Entities;
using System.Linq.Expressions;

namespace Medallion.Data.Tests.Entities;

internal class ExpressionEqualityVisitorTest
{
    [Test]
    public void TestCanDetectEqualExpressions()
    {
        ExpressionEqualityVisitor visitor = new();
        Assert.IsTrue(visitor.Equals(Create(1), Create(1)));
        Assert.IsTrue(visitor.Equals(Create(2), Create(3))); // differ only by closure field value
        Assert.IsTrue(visitor.Equals(Create(10), Create2(10))); // differ only by closure type
        Assert.IsTrue(visitor.Equals(Create(5), Create2(-5))); // differ only by closure field value and closure type
    }

    [Test]
    public void TestCanDetectNotEqualExpressions()
    {
        var k = 2;
        Expression<Func<int>> e = () => 2 * (k + 2);
        ExpressionEqualityVisitor visitor = new();
        Assert.IsFalse(visitor.Equals(e, Create(2)));
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
}
