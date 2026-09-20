namespace Sms.Migrations;

/// Production migration entrypoint. Run in CI/CD before rolling out the API:
///   dotnet run --project db/Sms.Migrations -- "&lt;connectionString&gt;"
/// or with the ConnectionStrings__Sql environment variable set. Idempotent (re-running is a no-op).
public static class MigrateCli
{
    public static int Main(string[] args)
    {
        var conn = ResolveConnectionString(args, Environment.GetEnvironmentVariable("ConnectionStrings__Sql"));
        if (string.IsNullOrWhiteSpace(conn))
        {
            Console.Error.WriteLine(
                "Usage: dotnet run --project db/Sms.Migrations -- \"<connectionString>\"  (or set ConnectionStrings__Sql)");
            return 1;
        }

        try
        {
            MigrationRunner.Run(conn);
            Console.WriteLine("Migrations applied successfully.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Migration failed: {ex.Message}");
            return 2;
        }
    }

    /// args[0] wins; otherwise the env var; otherwise null. Pure (no I/O) for testability.
    public static string? ResolveConnectionString(string[] args, string? envValue) =>
        args.Length > 0 && !string.IsNullOrWhiteSpace(args[0]) ? args[0] : envValue;
}
