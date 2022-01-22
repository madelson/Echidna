using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Medallion.Data;

internal static class Invariant
{
    [Conditional("DEBUG")]
    public static void Require([DoesNotReturnIf(false)] bool condition, [CallerArgumentExpression("condition")] string message = "")
    {
        if (!condition)
        {
            throw new InvalidOperationException($"Invariant violated: {message}");
        }
    }

    public static Exception ShouldNeverGetHere(string? message = null) =>
        throw new InvalidOperationException(message ?? "should never get here");
}
