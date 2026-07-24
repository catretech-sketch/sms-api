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
public class StudentBusTests(SqlServerFixture fx)
{
    private const string Key = "integration-test-signing-key-32-bytes-min!!";

    private WebApplicationFactory<Program> App() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("environment", "Production");
            b.UseSetting("ConnectionStrings:Sql", fx.ConnectionString);
            b.UseSetting("Jwt:SigningKey", Key);
        });

    private static HttpClient Client(WebApplicationFactory<Program> app, Guid tenantId, Guid userId, string role)
    {
        var jwt = new JwtTokenService(
            new JwtOptions { Issuer = "sms", Audience = "sms-apps", SigningKey = Key, AccessTokenMinutes = 15 },
            new SystemClock());
        var token = jwt.IssueAccess(userId, tenantId, [role], isPlatform: false);
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);
        return client;
    }

    private static HttpClient AdminClient(WebApplicationFactory<Program> app, Guid tenantId) =>
        Client(app, tenantId, Guid.NewGuid(), Policies.Principal);

    private static HttpClient ParentClient(WebApplicationFactory<Program> app, Guid tenantId, Guid userId) =>
        Client(app, tenantId, userId, Policies.StudentOrParent);

    private static async Task<JsonElement> Data(HttpResponseMessage res, HttpStatusCode expected)
    {
        res.StatusCode.Should().Be(expected);
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("data").Clone();
    }

    private static async Task Seed(string cs, Guid tenantId, Func<SqlConnection, Task> work)
    {
        await using var conn = new SqlConnection(cs);
        await conn.OpenAsync();
        await conn.ExecuteAsync("EXEC sp_set_session_context @key=N'TenantId', @value=@t", new { t = tenantId });
        await work(conn);
    }

    /// Seeds a bus with a live trip and one recent GPS ping. Returns the (busId, busNo).
    private static async Task<(Guid busId, string busNo)> SeedLiveBus(
        SqlConnection conn, Guid tenantId, string routeName, double lat, double lng)
    {
        var busId = Guid.NewGuid();
        var busNo = $"KA-{Guid.NewGuid():N}"[..12];
        var tripId = Guid.NewGuid();
        await conn.ExecuteAsync(
            "INSERT dbo.Buses (Id, TenantId, BusNo, RouteName) VALUES (@Id, @TenantId, @BusNo, @RouteName)",
            new { Id = busId, TenantId = tenantId, BusNo = busNo, RouteName = routeName });
        await conn.ExecuteAsync(
            "INSERT dbo.Trips (Id, TenantId, BusId, BusNo, Status, StartedAt) " +
            "VALUES (@Id, @TenantId, @BusId, @BusNo, 'live', @StartedAt)",
            new { Id = tripId, TenantId = tenantId, BusId = busId, BusNo = busNo, StartedAt = DateTime.UtcNow });
        await conn.ExecuteAsync(
            "INSERT dbo.TripPings (Id, TenantId, TripId, Lat, Lng, SpeedKmh, Heading, At) " +
            "VALUES (@Id, @TenantId, @TripId, @Lat, @Lng, 30, 45, @At)",
            new { Id = Guid.NewGuid(), TenantId = tenantId, TripId = tripId, Lat = lat, Lng = lng, At = DateTime.UtcNow });
        return (busId, busNo);
    }

    [Fact]
    public async Task Admin_assign_then_list_returns_student_on_bus()
    {
        await using var app = App();
        var tenantId = Guid.NewGuid();
        var studentId = Guid.NewGuid();
        Guid busId = default;

        await Seed(fx.ConnectionString, tenantId, async conn =>
        {
            (busId, _) = await SeedLiveBus(conn, tenantId, "Route A", 12.97, 77.59);
            await conn.ExecuteAsync(
                "INSERT dbo.Students (Id, TenantId, AdmissionNo, Name) VALUES (@Id, @TenantId, @A, @N)",
                new { Id = studentId, TenantId = tenantId, A = "ADM-1", N = "Alice Smith" });
        });

        var admin = AdminClient(app, tenantId);

        (await admin.PutAsJsonAsync($"/v1/transport/buses/{busId}/students/{studentId}", new { }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        var list = await Data(await admin.GetAsync($"/v1/transport/buses/{busId}/students"), HttpStatusCode.OK);
        list.GetArrayLength().Should().Be(1);
        list[0].GetProperty("student_name").GetString().Should().Be("Alice Smith");
        list[0].GetProperty("admission_no").GetString().Should().Be("ADM-1");
        list[0].GetProperty("initials").GetString().Should().Be("AS");
    }

    [Fact]
    public async Task Admin_assign_is_upsert_one_bus_per_student()
    {
        await using var app = App();
        var tenantId = Guid.NewGuid();
        var studentId = Guid.NewGuid();
        Guid bus1 = default, bus2 = default;

        await Seed(fx.ConnectionString, tenantId, async conn =>
        {
            (bus1, _) = await SeedLiveBus(conn, tenantId, "Route 1", 12.90, 77.50);
            (bus2, _) = await SeedLiveBus(conn, tenantId, "Route 2", 12.91, 77.51);
            await conn.ExecuteAsync(
                "INSERT dbo.Students (Id, TenantId, AdmissionNo, Name) VALUES (@Id, @TenantId, @A, @N)",
                new { Id = studentId, TenantId = tenantId, A = "ADM-2", N = "Bob Jones" });
        });

        var admin = AdminClient(app, tenantId);
        await admin.PutAsJsonAsync($"/v1/transport/buses/{bus1}/students/{studentId}", new { });
        await admin.PutAsJsonAsync($"/v1/transport/buses/{bus2}/students/{studentId}", new { });

        // Reassigned to bus2 only — bus1 roster is now empty.
        (await Data(await admin.GetAsync($"/v1/transport/buses/{bus1}/students"), HttpStatusCode.OK))
            .GetArrayLength().Should().Be(0);
        (await Data(await admin.GetAsync($"/v1/transport/buses/{bus2}/students"), HttpStatusCode.OK))
            .GetArrayLength().Should().Be(1);
    }

    [Fact]
    public async Task Admin_assign_unknown_bus_returns_404()
    {
        await using var app = App();
        var tenantId = Guid.NewGuid();
        var studentId = Guid.NewGuid();

        await Seed(fx.ConnectionString, tenantId, conn => conn.ExecuteAsync(
            "INSERT dbo.Students (Id, TenantId, AdmissionNo, Name) VALUES (@Id, @TenantId, @A, @N)",
            new { Id = studentId, TenantId = tenantId, A = "ADM-3", N = "Cara Lee" }));

        var admin = AdminClient(app, tenantId);
        (await admin.PutAsJsonAsync($"/v1/transport/buses/{Guid.NewGuid()}/students/{studentId}", new { }))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Admin_endpoints_403_for_parent()
    {
        await using var app = App();
        var tenantId = Guid.NewGuid();
        var parent = ParentClient(app, tenantId, Guid.NewGuid());
        (await parent.GetAsync($"/v1/transport/buses/{Guid.NewGuid()}/students"))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Parent_sees_only_own_tenant_child_bus_despite_identical_admission_no()
    {
        await using var app = App();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var parentAUserId = Guid.NewGuid();
        const string sharedAdmission = "S001"; // both schools reuse this admission number

        string busNoA = "", busNoB = "";

        await Seed(fx.ConnectionString, tenantA, async conn =>
        {
            var (busId, busNo) = await SeedLiveBus(conn, tenantA, "A-Route", 12.97, 77.59);
            busNoA = busNo;
            var studentId = Guid.NewGuid();
            await conn.ExecuteAsync(
                "INSERT dbo.Students (Id, TenantId, AdmissionNo, Name) VALUES (@Id, @TenantId, @A, @N)",
                new { Id = studentId, TenantId = tenantA, A = sharedAdmission, N = "Child A" });
            await conn.ExecuteAsync(
                "INSERT dbo.StudentBusAssignments (Id, TenantId, StudentId, BusId) VALUES (@Id, @TenantId, @S, @B)",
                new { Id = Guid.NewGuid(), TenantId = tenantA, S = studentId, B = busId });
            // The parent account, linked to the child via Users.StudentId (= admission number).
            await conn.ExecuteAsync(
                "INSERT dbo.Users (Id, TenantId, StudentId, IsPlatform, Status) VALUES (@Id, @TenantId, @Adm, 0, 'active')",
                new { Id = parentAUserId, TenantId = tenantA, Adm = sharedAdmission });
        });

        await Seed(fx.ConnectionString, tenantB, async conn =>
        {
            var (busId, busNo) = await SeedLiveBus(conn, tenantB, "B-Route", 12.97, 77.59); // identical GPS on purpose
            busNoB = busNo;
            var studentId = Guid.NewGuid();
            await conn.ExecuteAsync(
                "INSERT dbo.Students (Id, TenantId, AdmissionNo, Name) VALUES (@Id, @TenantId, @A, @N)",
                new { Id = studentId, TenantId = tenantB, A = sharedAdmission, N = "Child B" });
            await conn.ExecuteAsync(
                "INSERT dbo.StudentBusAssignments (Id, TenantId, StudentId, BusId) VALUES (@Id, @TenantId, @S, @B)",
                new { Id = Guid.NewGuid(), TenantId = tenantB, S = studentId, B = busId });
        });

        var parentA = ParentClient(app, tenantA, parentAUserId);
        var data = await Data(await parentA.GetAsync("/v1/me/children/bus"), HttpStatusCode.OK);

        data.GetArrayLength().Should().Be(1);
        data[0].GetProperty("student_name").GetString().Should().Be("Child A");
        data[0].GetProperty("bus_no").GetString().Should().Be(busNoA);
        data[0].GetProperty("bus_no").GetString().Should().NotBe(busNoB);
        data[0].GetProperty("route_name").GetString().Should().Be("A-Route");
    }

    [Fact]
    public async Task Parent_gets_empty_when_child_has_no_bus()
    {
        await using var app = App();
        var tenantId = Guid.NewGuid();
        var parentUserId = Guid.NewGuid();

        await Seed(fx.ConnectionString, tenantId, async conn =>
        {
            await conn.ExecuteAsync(
                "INSERT dbo.Students (Id, TenantId, AdmissionNo, Name) VALUES (@Id, @TenantId, @A, @N)",
                new { Id = Guid.NewGuid(), TenantId = tenantId, A = "S777", N = "Lonely Child" });
            await conn.ExecuteAsync(
                "INSERT dbo.Users (Id, TenantId, StudentId, IsPlatform, Status) VALUES (@Id, @TenantId, @Adm, 0, 'active')",
                new { Id = parentUserId, TenantId = tenantId, Adm = "S777" });
        });

        var parent = ParentClient(app, tenantId, parentUserId);
        (await Data(await parent.GetAsync("/v1/me/children/bus"), HttpStatusCode.OK))
            .GetArrayLength().Should().Be(0);
    }
}
