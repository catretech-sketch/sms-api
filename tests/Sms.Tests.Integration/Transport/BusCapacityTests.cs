using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Sms.Shared.Kernel.Auth;
using Sms.Shared.Kernel.Authz;
using Sms.Shared.Kernel.Time;

namespace Sms.Tests.Integration.Transport;

[Collection("sql")]
public class BusCapacityTests(SqlServerFixture fx)
{
    private const string Key = "integration-test-signing-key-32-bytes-min!!";

    private WebApplicationFactory<Program> App() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("environment", "Production");
            b.UseSetting("ConnectionStrings:Sql", fx.ConnectionString);
            b.UseSetting("Jwt:SigningKey", Key);
        });

    private static HttpClient PrincipalClient(WebApplicationFactory<Program> app, Guid tenantId, Guid userId)
    {
        var jwt = new JwtTokenService(
            new JwtOptions { Issuer = "sms", Audience = "sms-apps", SigningKey = Key, AccessTokenMinutes = 15 },
            new SystemClock());
        var token = jwt.IssueAccess(userId, tenantId, [Policies.Principal], isPlatform: false);
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);
        return client;
    }

    private static async Task Seed(string cs, Guid tenantId, Func<SqlConnection, Task> work)
    {
        await using var conn = new SqlConnection(cs);
        await conn.OpenAsync();
        await conn.ExecuteAsync("EXEC sp_set_session_context @key=N'TenantId', @value=@t", new { t = tenantId });
        await work(conn);
    }

    private static async Task<Guid> SeedBusAsync(string cs, Guid tenantId, int? capacity)
    {
        var busId = Guid.NewGuid();
        await Seed(cs, tenantId, conn => conn.ExecuteAsync(
            "INSERT dbo.Buses (Id, TenantId, BusNo, Capacity) VALUES (@Id, @TenantId, @BusNo, @Capacity)",
            new { Id = busId, TenantId = tenantId, BusNo = "CAP-01", Capacity = capacity }));
        return busId;
    }

    private static async Task<Guid> SeedStudentAsync(string cs, Guid tenantId, string name)
    {
        var studentId = Guid.NewGuid();
        await Seed(cs, tenantId, conn => conn.ExecuteAsync(
            "INSERT dbo.Students (Id, TenantId, Name, AdmissionNo) VALUES (@Id, @TenantId, @Name, @AdmissionNo)",
            new { Id = studentId, TenantId = tenantId, Name = name, AdmissionNo = Guid.NewGuid().ToString("N")[..10] }));
        return studentId;
    }

    [Fact]
    public async Task AssignStudent_succeeds_when_under_capacity()
    {
        await using var app = App();
        var tenantId = Guid.NewGuid();
        await TestTenancy.EnsureTenantAsync(fx.ConnectionString, tenantId, tier: "platinum");
        var busId = await SeedBusAsync(fx.ConnectionString, tenantId, capacity: 2);
        var studentId = await SeedStudentAsync(fx.ConnectionString, tenantId, "Rahul Sharma");

        var client = PrincipalClient(app, tenantId, Guid.NewGuid());
        var res = await client.PutAsJsonAsync($"/v1/transport/buses/{busId}/students/{studentId}", new { });

        res.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task AssignStudent_fails_with_409_when_at_capacity()
    {
        await using var app = App();
        var tenantId = Guid.NewGuid();
        await TestTenancy.EnsureTenantAsync(fx.ConnectionString, tenantId, tier: "platinum");
        var busId = await SeedBusAsync(fx.ConnectionString, tenantId, capacity: 1);
        var seated = await SeedStudentAsync(fx.ConnectionString, tenantId, "Amit Kumar");
        var overflow = await SeedStudentAsync(fx.ConnectionString, tenantId, "Priya Singh");

        var client = PrincipalClient(app, tenantId, Guid.NewGuid());
        (await client.PutAsJsonAsync($"/v1/transport/buses/{busId}/students/{seated}", new { }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        var res = await client.PutAsJsonAsync($"/v1/transport/buses/{busId}/students/{overflow}", new { });

        res.StatusCode.Should().Be(HttpStatusCode.Conflict);
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("error").GetProperty("code").GetString().Should().Be("capacity_reached");
    }

    [Fact]
    public async Task AssignStudent_succeeds_regardless_of_occupancy_when_capacity_is_null()
    {
        await using var app = App();
        var tenantId = Guid.NewGuid();
        await TestTenancy.EnsureTenantAsync(fx.ConnectionString, tenantId, tier: "platinum");
        var busId = await SeedBusAsync(fx.ConnectionString, tenantId, capacity: null);
        var s1 = await SeedStudentAsync(fx.ConnectionString, tenantId, "Student One");
        var s2 = await SeedStudentAsync(fx.ConnectionString, tenantId, "Student Two");

        var client = PrincipalClient(app, tenantId, Guid.NewGuid());
        (await client.PutAsJsonAsync($"/v1/transport/buses/{busId}/students/{s1}", new { }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await client.PutAsJsonAsync($"/v1/transport/buses/{busId}/students/{s2}", new { }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task AssignStudent_reassigning_seated_student_to_same_bus_never_false_blocks()
    {
        await using var app = App();
        var tenantId = Guid.NewGuid();
        await TestTenancy.EnsureTenantAsync(fx.ConnectionString, tenantId, tier: "platinum");
        var busId = await SeedBusAsync(fx.ConnectionString, tenantId, capacity: 1);
        var studentId = await SeedStudentAsync(fx.ConnectionString, tenantId, "Seated Student");
        var stopId = Guid.NewGuid();
        await Seed(fx.ConnectionString, tenantId, conn => conn.ExecuteAsync(
            "INSERT dbo.RouteStops (Id, TenantId, RouteId, Name, Seq) VALUES (@Id, @TenantId, @RouteId, @Name, 1)",
            new { Id = stopId, TenantId = tenantId, RouteId = Guid.NewGuid(), Name = "Stop A" }));

        var client = PrincipalClient(app, tenantId, Guid.NewGuid());
        (await client.PutAsJsonAsync($"/v1/transport/buses/{busId}/students/{studentId}", new { }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Re-save the same student on the same (now-full-by-themselves) bus, changing only their stop.
        var res = await client.PutAsJsonAsync(
            $"/v1/transport/buses/{busId}/students/{studentId}", new { stopId });

        res.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }
}
