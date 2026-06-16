using Microsoft.Extensions.DependencyInjection;
using Sms.Shared.Kernel.Data;
using Sms.Shared.Kernel.Tenancy;

namespace Sms.Api.Metrics;

/// Upserts the current month's platform metrics snapshot at startup (idempotent).
/// Historical months accumulate boot-over-boot; the current month is always refreshed.
public static class MetricsSnapshotWriter
{
    public static async Task RunAsync(WebApplication app)
    {
        var log = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("MetricsSnapshotWriter");
        await using var scope = app.Services.CreateAsyncScope();
        var tenant = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        tenant.Set(null, null, isPlatform: true);
        var factory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();

        await using var conn = await factory.OpenAsync();
        await Dapper.SqlMapper.ExecuteAsync(conn, new Dapper.CommandDefinition(
            "dbo.PlatformMetrics_UpsertCurrentMonth",
            commandType: System.Data.CommandType.StoredProcedure));
        log.LogInformation("Platform metrics snapshot upserted for the current month.");
    }
}
