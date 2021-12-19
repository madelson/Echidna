using Medallion.Data.Templating;

namespace Medallion.Data.Tests.Templating;

internal class SqlTemplateTest
{
    [Test]
    public void TestDefaultTemplateIsEmpty()
    {
        Assert.IsEmpty(default(SqlTemplate).Fragments);
    }

    [Test]
    public void TestEquality()
    {
        var template1 = new SqlTemplate($"{1:p}x{true:p}");
        var template2 = new SqlTemplate($"{1:p}x{true:p}");
        var template3 = new SqlTemplate($"{1:p}x{false:p}");

        Assert.IsTrue(template1.Equals(template2));
        Assert.IsTrue(template1.Equals((object)template2));
        Assert.IsTrue(template1.GetHashCode() == template2.GetHashCode());
        Assert.IsFalse(template1.Equals(template3));
        Assert.IsFalse(template1.Equals((object)template3));
        Assert.IsFalse(template1.GetHashCode() == template3.GetHashCode());
    }

    [Test]
    public void TestForEach()
    {
        var template = SqlTemplate.ForEach("abc", ch => $"{ch}x");
        CollectionAssert.AreEqual(
            new SqlTemplateFragment[]
            {
                new('a', SqlTemplateFragmentType.Default),
                new("x", SqlTemplateFragmentType.Raw),
                new('b', SqlTemplateFragmentType.Default),
                new("x", SqlTemplateFragmentType.Raw),
                new('c', SqlTemplateFragmentType.Default),
                new("x", SqlTemplateFragmentType.Raw),
            },
            template.Fragments
        );
    }

    [Test]
    public void TestForEachWithIndex()
    {
        var template = SqlTemplate.ForEach("abc", (ch, index) => $"{(index > 0 ? ", " : null):r}{ch:v}y");
        CollectionAssert.AreEqual(
            new SqlTemplateFragment[]
            {
                new(null, SqlTemplateFragmentType.Raw),
                new('a', SqlTemplateFragmentType.Value),
                new("y", SqlTemplateFragmentType.Raw),
                new(", ", SqlTemplateFragmentType.Raw),
                new('b', SqlTemplateFragmentType.Value),
                new("y", SqlTemplateFragmentType.Raw),
                new(", ", SqlTemplateFragmentType.Raw),
                new('c', SqlTemplateFragmentType.Value),
                new("y", SqlTemplateFragmentType.Raw),
            },
            template.Fragments
        );
    }
}
