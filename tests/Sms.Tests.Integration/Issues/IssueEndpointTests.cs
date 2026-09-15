using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Sms.Shared.Kernel.Auth;
using Sms.Shared.Kernel.Authz;
using Sms.Shared.Kernel.Time;
using Xunit;

namespace Sms.Tests.Integration.Issues;

[Collection("sql")]
public class IssueEndpointTests(SqlServerFixture fx)
{
    private const string Key = "integration-test-signing-key-32-bytes-min!!";

    private static WebApplicationFactory<Program> App(SqlServerFixture fx) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("environment", "Production");
            b.UseSetting("ConnectionStrings:Sql", fx.ConnectionString);
            b.UseSetting("Jwt:SigningKey", Key);
        });

    private static HttpClient ClientFor(WebApplicationFactory<Program> app, Guid tenantId, Guid userId, string role)
    {
        var jwt = new JwtTokenService(
            new JwtOptions { Issuer = "sms", Audience = "sms-apps", SigningKey = Key, AccessTokenMinutes = 15 },
            new SystemClock());
        var token = jwt.IssueAccess(userId, tenantId, new[] { role }, isPlatform: false);
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);
        return client;
    }

    private static async Task SeedUserAsync(SqlServerFixture fx, Guid tenantId, Guid userId, string name)
    {
        await using var conn = new Microsoft.Data.SqlClient.SqlConnection(fx.ConnectionString);
        await conn.OpenAsync();
        await conn.ExecuteAsync("EXEC sp_set_session_context @key=N'TenantId', @value=@tenantId", new { tenantId });
        await conn.ExecuteAsync(
            "INSERT dbo.Users (Id, TenantId, Name) VALUES (@userId, @tenantId, @name)",
            new { userId, tenantId, name });
    }

    private static async Task SeedManagerRoleAsync(SqlServerFixture fx, Guid userId, string role)
    {
        await using var conn = new Microsoft.Data.SqlClient.SqlConnection(fx.ConnectionString);
        await conn.OpenAsync();
        await conn.ExecuteAsync("INSERT dbo.UserRoles (UserId, Role) VALUES (@userId, @role)", new { userId, role });
    }

    [Fact]
    public async Task Driver_can_create_an_issue_and_it_is_visible_only_to_them_and_managers()
    {
        var tenantId = Guid.NewGuid();
        var driverId = Guid.NewGuid();
        var otherDriverId = Guid.NewGuid();
        var adminId = Guid.NewGuid();
        await SeedUserAsync(fx, tenantId, driverId, "Driver One");
        await SeedUserAsync(fx, tenantId, otherDriverId, "Driver Two");
        await SeedUserAsync(fx, tenantId, adminId, "School Admin");
        await SeedManagerRoleAsync(fx, adminId, Policies.SchoolAdmin);

        var app = App(fx);
        var driverClient = ClientFor(app, tenantId, driverId, "driver");
        var otherDriverClient = ClientFor(app, tenantId, otherDriverId, "driver");
        var adminClient = ClientFor(app, tenantId, adminId, Policies.SchoolAdmin);

        var create = await driverClient.PostAsJsonAsync("/v1/staff/issues", new
        {
            category = "safety",
            title = "Loose seatbelt on row 3",
            description = "Seatbelt buckle on the third row is broken.",
            priority = "high",
        });
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        using var createdDoc = JsonDocument.Parse(await create.Content.ReadAsStringAsync());
        var reporterUserId = createdDoc.RootElement.GetProperty("data").GetProperty("reporter_user_id").GetString();
        reporterUserId.Should().Be(driverId.ToString());

        var ownList = await driverClient.GetAsync("/v1/staff/issues");
        ownList.StatusCode.Should().Be(HttpStatusCode.OK);
        using var ownDoc = JsonDocument.Parse(await ownList.Content.ReadAsStringAsync());
        ownDoc.RootElement.GetProperty("data").GetArrayLength().Should().Be(1);

        var peerList = await otherDriverClient.GetAsync("/v1/staff/issues");
        using var peerDoc = JsonDocument.Parse(await peerList.Content.ReadAsStringAsync());
        peerDoc.RootElement.GetProperty("data").GetArrayLength().Should().Be(0);

        var managerList = await adminClient.GetAsync("/v1/staff/issues");
        using var managerDoc = JsonDocument.Parse(await managerList.Content.ReadAsStringAsync());
        managerDoc.RootElement.GetProperty("data").GetArrayLength().Should().Be(1);

        var adminNotifications = await adminClient.GetAsync("/v1/notifications");
        using var notifDoc = JsonDocument.Parse(await adminNotifications.Content.ReadAsStringAsync());
        var titles = notifDoc.RootElement.GetProperty("data").EnumerateArray()
            .Select(r => r.GetProperty("title").GetString()).ToList();
        titles.Should().Contain(t => t != null && t.Contains("Loose seatbelt on row 3"));
    }

    [Theory]
    [InlineData("driver")]
    [InlineData("conductor")]
    [InlineData("sweeper")]
    [InlineData("gardener")]
    [InlineData("guard")]
    [InlineData("peon")]
    public async Task All_six_staff_roles_can_create_an_issue(string role)
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        await SeedUserAsync(fx, tenantId, userId, $"Staff {role}");
        var client = ClientFor(App(fx), tenantId, userId, role);

        var create = await client.PostAsJsonAsync("/v1/staff/issues", new
        {
            category = "other",
            title = $"{role} reported issue",
            description = "Description text.",
            priority = "normal",
        });
        create.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Reporter_is_always_taken_from_the_authenticated_token_not_the_request_body()
    {
        var tenantId = Guid.NewGuid();
        var driverId = Guid.NewGuid();
        var impersonatedId = Guid.NewGuid();
        await SeedUserAsync(fx, tenantId, driverId, "Real Driver");
        var client = ClientFor(App(fx), tenantId, driverId, "driver");

        var create = await client.PostAsJsonAsync("/v1/staff/issues", new
        {
            category = "other",
            title = "Attempted spoof",
            description = "Trying to set someone else as reporter.",
            priority = "normal",
            reporter_user_id = impersonatedId,
        });
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        using var doc = JsonDocument.Parse(await create.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("data").GetProperty("reporter_user_id").GetString().Should().Be(driverId.ToString());
    }

    [Fact]
    public async Task Cross_tenant_issues_are_never_visible()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();
        await SeedUserAsync(fx, tenantA, userA, "Tenant A User");
        await SeedUserAsync(fx, tenantB, userB, "Tenant B Admin");
        await SeedManagerRoleAsync(fx, userB, Policies.SchoolAdmin);

        var app = App(fx);
        var clientA = ClientFor(app, tenantA, userA, "driver");
        var clientB = ClientFor(app, tenantB, userB, Policies.SchoolAdmin);

        await clientA.PostAsJsonAsync("/v1/staff/issues", new
        {
            category = "other", title = "Tenant A issue", description = "Description.", priority = "normal",
        });

        var listB = await clientB.GetAsync("/v1/staff/issues");
        using var docB = JsonDocument.Parse(await listB.Content.ReadAsStringAsync());
        docB.RootElement.GetProperty("data").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task Non_manager_cannot_patch_an_issue()
    {
        var tenantId = Guid.NewGuid();
        var driverId = Guid.NewGuid();
        await SeedUserAsync(fx, tenantId, driverId, "Driver");
        var client = ClientFor(App(fx), tenantId, driverId, "driver");

        var create = await client.PostAsJsonAsync("/v1/staff/issues", new
        {
            category = "other", title = "Issue", description = "Description.", priority = "normal",
        });
        using var doc = JsonDocument.Parse(await create.Content.ReadAsStringAsync());
        var id = doc.RootElement.GetProperty("data").GetProperty("id").GetString();

        var patch = await client.PatchAsJsonAsync($"/v1/issues/{id}", new { status = "resolved" });
        patch.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Manager_can_patch_status_and_add_a_note()
    {
        var tenantId = Guid.NewGuid();
        var driverId = Guid.NewGuid();
        var adminId = Guid.NewGuid();
        await SeedUserAsync(fx, tenantId, driverId, "Driver");
        await SeedUserAsync(fx, tenantId, adminId, "Admin");
        await SeedManagerRoleAsync(fx, adminId, Policies.SchoolAdmin);
        var app = App(fx);
        var driverClient = ClientFor(app, tenantId, driverId, "driver");
        var adminClient = ClientFor(app, tenantId, adminId, Policies.SchoolAdmin);

        var create = await driverClient.PostAsJsonAsync("/v1/staff/issues", new
        {
            category = "other", title = "Issue", description = "Description.", priority = "normal",
        });
        using var doc = JsonDocument.Parse(await create.Content.ReadAsStringAsync());
        var id = doc.RootElement.GetProperty("data").GetProperty("id").GetString();

        var patch = await adminClient.PatchAsJsonAsync($"/v1/issues/{id}", new { status = "in_progress", note = "Looking into it." });
        patch.StatusCode.Should().Be(HttpStatusCode.OK);
        using var patchDoc = JsonDocument.Parse(await patch.Content.ReadAsStringAsync());
        patchDoc.RootElement.GetProperty("data").GetProperty("status").GetString().Should().Be("in_progress");

        var detail = await adminClient.GetAsync($"/v1/staff/issues/{id}");
        using var detailDoc = JsonDocument.Parse(await detail.Content.ReadAsStringAsync());
        detailDoc.RootElement.GetProperty("data").GetProperty("notes").GetArrayLength().Should().Be(1);
    }

    [Fact]
    public async Task Invalid_status_value_is_rejected()
    {
        var tenantId = Guid.NewGuid();
        var driverId = Guid.NewGuid();
        var adminId = Guid.NewGuid();
        await SeedUserAsync(fx, tenantId, driverId, "Driver");
        await SeedUserAsync(fx, tenantId, adminId, "Admin");
        await SeedManagerRoleAsync(fx, adminId, Policies.SchoolAdmin);
        var app = App(fx);
        var driverClient = ClientFor(app, tenantId, driverId, "driver");
        var adminClient = ClientFor(app, tenantId, adminId, Policies.SchoolAdmin);

        var create = await driverClient.PostAsJsonAsync("/v1/staff/issues", new
        {
            category = "other", title = "Issue", description = "Description.", priority = "normal",
        });
        using var doc = JsonDocument.Parse(await create.Content.ReadAsStringAsync());
        var id = doc.RootElement.GetProperty("data").GetProperty("id").GetString();

        var patch = await adminClient.PatchAsJsonAsync($"/v1/issues/{id}", new { status = "bogus_status" });
        patch.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Reporting_against_a_trip_that_does_not_belong_to_the_caller_is_rejected()
    {
        var tenantId = Guid.NewGuid();
        var driverId = Guid.NewGuid();
        var otherDriverId = Guid.NewGuid();
        await SeedUserAsync(fx, tenantId, driverId, "Driver");
        await SeedUserAsync(fx, tenantId, otherDriverId, "Other Driver");
        var app = App(fx);
        var otherDriverClient = ClientFor(app, tenantId, otherDriverId, "driver");
        var driverClient = ClientFor(app, tenantId, driverId, "driver");

        // NOTE: the request body uses snake_case (bus_no) not camelCase (busNo), because the
        // main MVC pipeline's JSON options apply SnakeCaseNamingPolicy (see
        // Sms.Api/Extensions/ServiceCollectionExtensions.cs, AddJsonOptions) to both
        // serialization and deserialization of StartTripRequest(Guid? RouteId, string? BusNo,
        // string Direction). The brief's original draft used busNo, which would bind to null.
        var startTrip = await otherDriverClient.PostAsJsonAsync("/v1/staff/trips", new { bus_no = "BUS-1", direction = "pickup" });
        startTrip.StatusCode.Should().Be(HttpStatusCode.Created);
        using var tripDoc = JsonDocument.Parse(await startTrip.Content.ReadAsStringAsync());
        var tripId = tripDoc.RootElement.GetProperty("data").GetProperty("id").GetString();

        var create = await driverClient.PostAsJsonAsync("/v1/staff/issues", new
        {
            category = "vehicle", title = "Issue", description = "Description.", priority = "normal", trip_id = tripId,
        });
        create.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Two_rapid_submissions_create_two_distinct_issues_no_deduplication_for_mvp()
    {
        var tenantId = Guid.NewGuid();
        var driverId = Guid.NewGuid();
        await SeedUserAsync(fx, tenantId, driverId, "Driver");
        var client = ClientFor(App(fx), tenantId, driverId, "driver");

        var body = new { category = "other", title = "Duplicate?", description = "Same text twice.", priority = "normal" };
        var first = await client.PostAsJsonAsync("/v1/staff/issues", body);
        var second = await client.PostAsJsonAsync("/v1/staff/issues", body);
        first.StatusCode.Should().Be(HttpStatusCode.Created);
        second.StatusCode.Should().Be(HttpStatusCode.Created);

        var list = await client.GetAsync("/v1/staff/issues");
        using var doc = JsonDocument.Parse(await list.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("data").GetArrayLength().Should().Be(2);
    }
}
