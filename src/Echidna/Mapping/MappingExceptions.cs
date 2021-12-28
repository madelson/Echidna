using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Medallion.Data.Mapping;

internal class LossyNumericConversionException : Exception
{
    public LossyNumericConversionException()
        : base("Numeric conversion would result in loss of precision or range") { }
}

internal class LossyNumericToBooleanConversionException : Exception
{
    public LossyNumericToBooleanConversionException()
        : base("Cannot convert numeric values other than 0 and 1 to booleans") { }
}