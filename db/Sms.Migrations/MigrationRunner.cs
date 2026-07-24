using FluentMigrator.Runner;
using Microsoft.Extensions.DependencyInjection;

namespace Sms.Migrations;

public static class MigrationRunner
{
    public static void Run(string connectionString)
    {
        var services = new ServiceCollection()
            .AddFluentMigratorCore()
            .ConfigureRunner(rb => rb
                .AddSqlServer()
                .WithGlobalConnectionString(connectionString)
                .ScanIn(typeof(MigrationRunner).Assembly).For.Migrations())
            .BuildServiceProvider(false);

        using var scope = services.CreateScope();
        scope.ServiceProvider.GetRequiredService<IMigrationRunner>().MigrateUp();
    }

    /// <summary>
    /// Migrates up to (and including) a specific version. Used by tests that need to insert
    /// data BETWEEN two migrations — e.g. inserting pre-existing rows after the schema
    /// migration but before the backfill migration runs, to prove the backfill's real
    /// data effect end-to-end rather than against hand-copied SQL snippets.
    /// </summary>
    public static void RunTo(string connectionString, long version)
    {
        var services = new ServiceCollection()
            .AddFluentMigratorCore()
            .ConfigureRunner(rb => rb
                .AddSqlServer()
                .WithGlobalConnectionString(connectionString)
                .ScanIn(typeof(MigrationRunner).Assembly).For.Migrations())
            .BuildServiceProvider(false);

        using var scope = services.CreateScope();
        scope.ServiceProvider.GetRequiredService<IMigrationRunner>().MigrateUp(version);
    }
}
