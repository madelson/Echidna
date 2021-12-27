using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace Medallion.Data;

internal static class Invariant
{
    [Conditional("DEBUG")]
    public static void Require(bool condition, string? message = null)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message ?? "invariant violated");
        }
    }

    public static Exception ShouldNeverGetHere(string? message = null) =>
        throw new InvalidOperationException(message ?? "should never get here");
}
