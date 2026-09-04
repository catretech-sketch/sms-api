using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Dapper;
using Sms.Modules.Transport;
using Sms.Shared.Kernel.Tenancy;
using Xunit;
using FluentAssertions;

namespace Sms.Tests.Integration.Transport;

[Collection("sql")]
public class BusLiveSnapshotTests(SqlServerFixture fx)
{
    private WebApplicationFactory<Program> App() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("environment", "Production");
            b.UseSetting("ConnectionStrings:Sql", fx.ConnectionString);
        });

    private async Task<(Guid tenantId, Guid busId, Guid tripId)> SeedBusWithLastPing(DateTime pingAt, double speedKmh)
    {
        var tenantId = Guid.NewGuid();
        var busId = Guid.NewGuid();
        var tripId = Guid.NewGuid();
        await using var conn = new SqlConnection(fx.ConnectionString);
        await conn.OpenAsync();
        await conn.ExecuteAsync("EXEC sp_set_session_context @key=N'TenantId', @value=@t", new { t = tenantId });
        await conn.ExecuteAsync(
            "INSERT INTO dbo.Buses (Id, TenantId, BusNo) VALUES (@Id, @TenantId, 'BUS-1')",
            new { Id = busId, TenantId = tenantId });
        await conn.ExecuteAsync(
            @"INSERT INTO dbo.Trips (Id, TenantId, BusId, Direction, Status, StartedAt)
              VALUES (@Id, @TenantId, @BusId, 'pickup', 'live', SYSUTCDATETIME())",
            new { Id = tripId, TenantId = tenantId, BusId = busId });
        await conn.ExecuteAsync(
            @"INSERT INTO dbo.TripPings (Id, TenantId, TripId, Lat, Lng, SpeedKmh, Heading, At)
              VALUES (@Id, @TenantId, @TripId, 12.1, 77.1, @SpeedKmh, 90, @At)",
            new { Id = Guid.NewGuid(), TenantId = tenantId, TripId = tripId, SpeedKmh = speedKmh, At = pingAt });
        return (tenantId, busId, tripId);
    }

    [Fact]
    public async Task Status_is_moving_when_a_fresh_fast_ping_exists()
    {
        var (tenantId, busId, _) = await SeedBusWithLastPing(DateTime.UtcNow.AddSeconds(-5), speedKmh: 25);
        await using var app = App();
        using var scope = app.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContext>().Set(tenantId, null, isPlatform: false);
        var repo = scope.ServiceProvider.GetRequiredService<BusRepository>();

        var snapshot = await repo.GetLiveSnapshotAsync(busId, default);

        snapshot.Status.Should().Be("moving");
        snapshot.Lat.Should().Be(12.1);
        snapshot.SpeedKmh.Should().Be(25);
    }

    [Fact]
    public async Task Status_is_stopped_when_a_fresh_slow_ping_exists()
    {
        var (tenantId, busId, _) = await SeedBusWithLastPing(DateTime.UtcNow.AddSeconds(-5), speedKmh: 1);
        await using var app = App();
        using var scope = app.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContext>().Set(tenantId, null, isPlatform: false);
        var repo = scope.ServiceProvider.GetRequiredService<BusRepository>();

        (await repo.GetLiveSnapshotAsync(busId, default)).Status.Should().Be("stopped");
    }

    [Fact]
    public async Task Status_is_offline_when_the_last_ping_is_older_than_60_seconds()
    {
        var (tenantId, busId, _) = await SeedBusWithLastPing(DateTime.UtcNow.AddSeconds(-90), speedKmh: 25);
        await using var app = App();
        using var scope = app.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContext>().Set(tenantId, null, isPlatform: false);
        var repo = scope.ServiceProvider.GetRequiredService<BusRepository>();

        (await repo.GetLiveSnapshotAsync(busId, default)).Status.Should().Be("offline");
    }

    [Fact]
    public async Task Status_is_offline_when_the_bus_has_never_pinged()
    {
        var tenantId = Guid.NewGuid();
        var busId = Guid.NewGuid();
        await using (var conn = new SqlConnection(fx.ConnectionString))
        {
            await conn.OpenAsync();
            await conn.ExecuteAsync("EXEC sp_set_session_context @key=N'TenantId', @value=@t", new { t = tenantId });
            await conn.ExecuteAsync(
                "INSERT INTO dbo.Buses (Id, TenantId, BusNo) VALUES (@Id, @TenantId, 'BUS-1')",
                new { Id = busId, TenantId = tenantId });
        }
        await using var app = App();
        using var scope = app.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContext>().Set(tenantId, null, isPlatform: false);
        var repo = scope.ServiceProvider.GetRequiredService<BusRepository>();

        var snapshot = await repo.GetLiveSnapshotAsync(busId, default);
        snapshot.Status.Should().Be("offline");
        snapshot.Lat.Should().BeNull();
    }
}
