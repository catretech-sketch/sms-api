using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Dapper;
using Sms.Application.Services.Transport;
using Sms.Shared.Kernel.Authz;
using Xunit;
using FluentAssertions;

namespace Sms.Tests.Integration.Transport;

[Collection("sql")]
public class TransportAuthorizationResolverTests(SqlServerFixture fx)
{
    private WebApplicationFactory<Program> App() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("environment", "Production");
            b.UseSetting("ConnectionStrings:Sql", fx.ConnectionString);
        });

    [Fact]
    public async Task Principal_can_view_any_bus_in_their_own_tenant_but_not_another_tenants()
    {
        var tenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        var busId = Guid.NewGuid();
        var principalId = Guid.NewGuid();

        await using (var conn = new SqlConnection(fx.ConnectionString))
        {
            await conn.OpenAsync();
            await conn.ExecuteAsync("EXEC sp_set_session_context @key=N'TenantId', @value=@t", new { t = tenantId });
            await conn.ExecuteAsync("INSERT INTO dbo.Buses (Id, TenantId, BusNo) VALUES (@Id, @TenantId, 'BUS-1')",
                new { Id = busId, TenantId = tenantId });
        }

        await using var app = App();
        using var scope = app.Services.CreateScope();
        var resolver = scope.ServiceProvider.GetRequiredService<ITransportAuthorizationResolver>();

        (await resolver.CanViewBusAsync(principalId, tenantId, [Policies.Principal], busId, default)).Should().BeTrue();
        (await resolver.CanViewBusAsync(principalId, otherTenantId, [Policies.Principal], busId, default)).Should().BeFalse();
    }

    [Fact]
    public async Task Teacher_can_view_only_their_assigned_duty_bus()
    {
        var tenantId = Guid.NewGuid();
        var busId = Guid.NewGuid();
        var otherBusId = Guid.NewGuid();
        var teacherId = Guid.NewGuid();

        await using (var conn = new SqlConnection(fx.ConnectionString))
        {
            await conn.OpenAsync();
            await conn.ExecuteAsync("EXEC sp_set_session_context @key=N'TenantId', @value=@t", new { t = tenantId });
            await conn.ExecuteAsync(
                "INSERT INTO dbo.Buses (Id, TenantId, BusNo) VALUES (@Id, @TenantId, 'BUS-1'), (@OtherId, @TenantId, 'BUS-2')",
                new { Id = busId, OtherId = otherBusId, TenantId = tenantId });
            await conn.ExecuteAsync(
                "INSERT INTO dbo.BusAssignments (Id, TenantId, TeacherUserId, BusId) VALUES (@Id, @TenantId, @TeacherUserId, @BusId)",
                new { Id = Guid.NewGuid(), TenantId = tenantId, TeacherUserId = teacherId, BusId = busId });
        }

        await using var app = App();
        using var scope = app.Services.CreateScope();
        var resolver = scope.ServiceProvider.GetRequiredService<ITransportAuthorizationResolver>();

        (await resolver.CanViewBusAsync(teacherId, tenantId, [Policies.Teacher], busId, default)).Should().BeTrue();
        (await resolver.CanViewBusAsync(teacherId, tenantId, [Policies.Teacher], otherBusId, default)).Should().BeFalse();
    }

    [Fact]
    public async Task Driver_can_view_only_the_bus_of_their_own_active_trip()
    {
        var tenantId = Guid.NewGuid();
        var busId = Guid.NewGuid();
        var otherBusId = Guid.NewGuid();
        var driverId = Guid.NewGuid();

        await using (var conn = new SqlConnection(fx.ConnectionString))
        {
            await conn.OpenAsync();
            await conn.ExecuteAsync("EXEC sp_set_session_context @key=N'TenantId', @value=@t", new { t = tenantId });
            await conn.ExecuteAsync(
                "INSERT INTO dbo.Buses (Id, TenantId, BusNo) VALUES (@Id, @TenantId, 'BUS-1'), (@OtherId, @TenantId, 'BUS-2')",
                new { Id = busId, OtherId = otherBusId, TenantId = tenantId });
            await conn.ExecuteAsync(
                @"INSERT INTO dbo.Trips (Id, TenantId, BusId, DriverId, Direction, Status, StartedAt)
                  VALUES (@Id, @TenantId, @BusId, @DriverId, 'pickup', 'live', SYSUTCDATETIME())",
                new { Id = Guid.NewGuid(), TenantId = tenantId, BusId = busId, DriverId = driverId });
        }

        await using var app = App();
        using var scope = app.Services.CreateScope();
        var resolver = scope.ServiceProvider.GetRequiredService<ITransportAuthorizationResolver>();

        (await resolver.CanViewBusAsync(driverId, tenantId, [Policies.Driver], busId, default)).Should().BeTrue();
        (await resolver.CanViewBusAsync(driverId, tenantId, [Policies.Driver], otherBusId, default)).Should().BeFalse();
    }

    [Fact]
    public async Task Parent_can_view_only_their_own_childs_bus()
    {
        var tenantId = Guid.NewGuid();
        var busId = Guid.NewGuid();
        var otherBusId = Guid.NewGuid();
        var parentId = Guid.NewGuid();
        var studentId = Guid.NewGuid();
        const string admissionNo = "ADM-PARENT-001";

        await using (var conn = new SqlConnection(fx.ConnectionString))
        {
            await conn.OpenAsync();
            await conn.ExecuteAsync("EXEC sp_set_session_context @key=N'TenantId', @value=@t", new { t = tenantId });
            await conn.ExecuteAsync(
                "INSERT INTO dbo.Buses (Id, TenantId, BusNo) VALUES (@Id, @TenantId, 'BUS-1'), (@OtherId, @TenantId, 'BUS-2')",
                new { Id = busId, OtherId = otherBusId, TenantId = tenantId });
            await conn.ExecuteAsync(
                "INSERT INTO dbo.Students (Id, TenantId, Name, AdmissionNo) VALUES (@Id, @TenantId, 'Kid', @AdmissionNo)",
                new { Id = studentId, TenantId = tenantId, AdmissionNo = admissionNo });
            await conn.ExecuteAsync(
                "INSERT INTO dbo.StudentBusAssignments (Id, TenantId, StudentId, BusId) VALUES (@Id, @TenantId, @StudentId, @BusId)",
                new { Id = Guid.NewGuid(), TenantId = tenantId, StudentId = studentId, BusId = busId });
            // The parent's Users row is linked to their child via Users.StudentId = admission number
            // (see StudentBusService.GetMyChildrenBusAsync for this same pattern). dbo.Users (see
            // M0001_Foundation_Tables) has no NOT NULL columns besides its defaulted Id/IsPlatform/
            // Status/CreatedAt, and no Role column at all (roles live in dbo.UserRoles) — so only
            // Id/TenantId/StudentId/Email need to be supplied here.
            await conn.ExecuteAsync(
                "INSERT INTO dbo.Users (Id, TenantId, StudentId, Email) VALUES (@Id, @TenantId, @AdmissionNo, @Email)",
                new { Id = parentId, TenantId = tenantId, AdmissionNo = admissionNo, Email = $"parent-{parentId}@test.local" });
        }

        await using var app = App();
        using var scope = app.Services.CreateScope();
        var resolver = scope.ServiceProvider.GetRequiredService<ITransportAuthorizationResolver>();

        (await resolver.CanViewBusAsync(parentId, tenantId, [Policies.StudentOrParent], busId, default)).Should().BeTrue();
        (await resolver.CanViewBusAsync(parentId, tenantId, [Policies.StudentOrParent], otherBusId, default)).Should().BeFalse();
    }

    [Fact]
    public async Task Unknown_role_is_denied()
    {
        var tenantId = Guid.NewGuid();
        var busId = Guid.NewGuid();
        await using (var conn = new SqlConnection(fx.ConnectionString))
        {
            await conn.OpenAsync();
            await conn.ExecuteAsync("EXEC sp_set_session_context @key=N'TenantId', @value=@t", new { t = tenantId });
            await conn.ExecuteAsync("INSERT INTO dbo.Buses (Id, TenantId, BusNo) VALUES (@Id, @TenantId, 'BUS-1')",
                new { Id = busId, TenantId = tenantId });
        }
        await using var app = App();
        using var scope = app.Services.CreateScope();
        var resolver = scope.ServiceProvider.GetRequiredService<ITransportAuthorizationResolver>();

        (await resolver.CanViewBusAsync(Guid.NewGuid(), tenantId, ["some.other.role"], busId, default)).Should().BeFalse();
    }
}
