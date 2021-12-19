using Medallion.Data.Templating;
using static Medallion.Data.SqlTemplate;

namespace Medallion.Data.Tests.Templating;

internal class ConditionalSqlTemplateTest
{
    private readonly List<int> _evaluations = new();

    [SetUp]
    public void SetUp() => this._evaluations.Clear();

    [Test]
    public void TestShortCircuitsEvaluation()
    {
        _ = If(false, $"{this.Eval(1)} {this.Eval(2)}");
        Assert.IsEmpty(this._evaluations);
    }

    [Test]
    public void TestShortCircuitsEvaluationWhenChained()
    {
        _ = If(true, $"{this.Eval(1)}") || If(true, $"{this.Eval(2)}") || Else($"{this.Eval(3)}");
        CollectionAssert.AreEqual(new[] { 1 }, this._evaluations);
    }

    [Test]
    public void TestChainFallsThroughToElseClause()
    {
        _ = If(false, $"{this.Eval(1)}") || If(false, $"{this.Eval(2)}") || Else($"{this.Eval(3)}");
        CollectionAssert.AreEqual(new[] { 3 }, this._evaluations);
    }

    [Test]
    public void TestCanEmbedSingleIfInInterpolatedStringTemplate()
    {
        var template = new SqlTemplate($"{If(true, $"{this.Eval(1)}")} {If(false, $"{this.Eval(2)}")}");
        CollectionAssert.AreEqual(
            new SqlTemplateFragment[]
            {
                new(1, SqlTemplateFragmentType.Default),
                new(" ", SqlTemplateFragmentType.Raw),
            },
            template.Fragments
        );

        CollectionAssert.AreEqual(new[] { 1 }, this._evaluations);
    }

    [Test]
    public void TestCanEmbedIfElseInInterpolatedStringTemplate()
    {
        var template = new SqlTemplate($"{If(false, $"{this.Eval(1)}") || Else($"{this.Eval(2)}")} {If(false, $"{this.Eval(3)}") || If(false, $"{this.Eval(4)}")}");
        CollectionAssert.AreEqual(
            new SqlTemplateFragment[]
            {
                new(2, SqlTemplateFragmentType.Default),
                new(" ", SqlTemplateFragmentType.Raw),
            },
            template.Fragments
        );

        CollectionAssert.AreEqual(new[] { 2 }, this._evaluations);
    }

    [Test]
    public void TestMisuseOfOrOperatorThrows()
    {
        Assert.Throws<InvalidOperationException>(() => { _ = If(true, $"a") | Else($"v"); });
    }

    private int Eval(int value)
    {
        this._evaluations.Add(value);
        return value;
    }
}
