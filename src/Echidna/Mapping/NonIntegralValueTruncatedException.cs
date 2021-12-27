using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Medallion.Data.Mapping;

internal sealed class NonIntegralValueTruncatedException : Exception
{
    public NonIntegralValueTruncatedException()
        : base("Floating point or decimal value would be truncated by the conversion") { }
}
