using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Medallion.Data.Mapping;

internal class LossyConversionException : Exception
{
    public static ConstructorInfo DefaultConstructor { get; } =
        typeof(LossyConversionException).GetConstructor(Type.EmptyTypes) ?? throw Invariant.ShouldNeverGetHere();

    public LossyConversionException()
        : this("Conversion between scalar types would result in a loss of information, precision, or range") { }

    public LossyConversionException(string message) : base(message) { }
}