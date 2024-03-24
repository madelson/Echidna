namespace Medallion.Data.Tests;

internal static class TestHelper
{
    public static bool IsOnCIPipeline => bool.TryParse(Environment.GetEnvironmentVariable("CI"), out var parsed) && parsed;

    public static IEnumerable<Db> DbsToTest()
    {
        var allDbs = Enum.GetValues(typeof(Db)).OfType<Db>();
        return IsOnCIPipeline
            ? allDbs.Except(new[] { Db.Oracle, Db.MariaDb })
            : allDbs;
    }
}