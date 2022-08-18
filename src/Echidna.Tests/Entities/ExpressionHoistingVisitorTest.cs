using Medallion.Data.Entities;
using System.Linq.Expressions;

namespace Medallion.Data.Tests.Entities;

internal class ExpressionHoistingVisitorTest
{
    private readonly ExpressionHoistingVisitor _visitor = new();

    [Test]
    public void TestDoesNotHoistConstant()
    {
        Expression<Func<int, int>> expression = i => i + 2;
        Assert.AreSame(expression, this._visitor.Hoist(expression));
    }

    [Test]
    public void TestHoistsSubExpressions()
    {
        Expression<Func<int, string>> expression = i => string.Join(new string('a', 10), Enumerable.Repeat(TimeSpan.Zero.ToString(), i));
        var hoisted = this._visitor.Hoist(expression);
        Assert.AreEqual("i => Join(Invoke(value(System.Func`1[System.String])), Repeat(Invoke(value(System.Func`1[System.String])), i))", hoisted.ToString());
    }
}
