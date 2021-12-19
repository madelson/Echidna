using Medallion.Data.Templating;
using System.Collections.Immutable;

namespace Medallion.Data.Tests.Templating
{
    internal class InterpolatedStringSqlTemplateTest
    {
        [Test]
        public void TestWorksWithNoHoles()
        {
            var fragments = Template($"no holes here!");
            CollectionAssert.AreEqual(
                new[] { new SqlTemplateFragment("no holes here!", SqlTemplateFragmentType.Raw) },
                fragments
            );
        }

        [Test]
        public void TestCapturesFormats()
        {
            var fragments = Template($"{1:p}{2:r}{3:v}");
            CollectionAssert.AreEqual(
                new SqlTemplateFragment[] 
                { 
                    new(1, SqlTemplateFragmentType.Parameter),
                    new(2, SqlTemplateFragmentType.Raw),
                    new(3, SqlTemplateFragmentType.Value),
                },
                fragments
            );
        }

        [Test]
        public void TestAppendTemplate()
        {
            var template = new SqlTemplate($"{1}/{2:v}");
            var fragments = Template($"{template}x{template}x{0}");
            CollectionAssert.AreEqual(
                new SqlTemplateFragment[]
                {
                    new(1, SqlTemplateFragmentType.Default),
                    new("/", SqlTemplateFragmentType.Raw),
                    new(2, SqlTemplateFragmentType.Value),
                    new("x", SqlTemplateFragmentType.Raw),
                    new(1, SqlTemplateFragmentType.Default),
                    new("/", SqlTemplateFragmentType.Raw),
                    new(2, SqlTemplateFragmentType.Value),
                    new("x", SqlTemplateFragmentType.Raw),
                    new(0, SqlTemplateFragmentType.Default),
                },
                fragments
            );
        }

        [Test]
        public void TestAppendTemplateEnumerable()
        {
            var templates = new SqlTemplate[] { new($"a"), new($"{1}{2}"), new($"{"b"}") };
            var fragments = Template($"({templates})");
            CollectionAssert.AreEqual(
                new SqlTemplateFragment[]
                {
                    new("(", SqlTemplateFragmentType.Raw),
                    new("a", SqlTemplateFragmentType.Raw),
                    new(1, SqlTemplateFragmentType.Default),
                    new(2, SqlTemplateFragmentType.Default),
                    new("b", SqlTemplateFragmentType.Default),
                    new(")", SqlTemplateFragmentType.Raw),
                },
                fragments
            );
        }

        [Test]
        public void TestImplicitlyConvertsFromSqlTemplate()
        {
            var fragments = Template(new SqlTemplate($"select {10}"));
            CollectionAssert.AreEqual(
                new SqlTemplateFragment[]
                {
                    new("select ", SqlTemplateFragmentType.Raw),
                    new(10, SqlTemplateFragmentType.Default),
                },
                fragments
            );
        }

        [Test]
        public void TestRejectsInvalidFormat()
        {
            Assert.Throws<FormatException>(() => Template($"{1:x}"));
        }

        [Test]
        public void TestRejectsBadFormats()
        {
            Assert.Throws<FormatException>(() => Template($"{default(SqlTemplate):p}"));
            Assert.Throws<FormatException>(() => Template($"{Array.Empty<SqlTemplate>():p}"));
        }

        [Test]
        public void TestCannotBeUsedOnceConsumed()
        {
            InterpolatedStringSqlTemplate template = $"select 1";
            var fragments = template.ToFragmentsAndClear();
            try { template.ToFragmentsAndClear(); }
            catch (InvalidOperationException) { return; }
            Assert.Fail("expected InvalidOperationException");
        }

        private static ImmutableArray<SqlTemplateFragment> Template(InterpolatedStringSqlTemplate template) => template.ToFragmentsAndClear();
    }
}
