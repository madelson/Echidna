using Medallion.Data.Mapping;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Medallion.Data.Tests;

internal class AssemblySetUpTest
{
    [Test]
    public void TestDoesNotReferenceSystemLinqExpressions()
    {
        Assert.That(
            typeof(ScalarConverter).Assembly.GetReferencedAssemblies().Select(a => a.Name),
            Does.Not.Contain("System.Linq.Expressions")
        );
    }
}
