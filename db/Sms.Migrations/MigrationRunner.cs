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
}
