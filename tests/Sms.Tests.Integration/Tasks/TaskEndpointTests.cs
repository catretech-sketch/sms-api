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

namespace Sms.Tests.Integration.Tasks;

[Collection("sql")]
public class TaskEndpointTests(SqlServerFixture fx)
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

    /// Links a user to a dbo.Staff row with the given free-text designation (e.g. "Driver"),
    /// which is how TaskRepository.GetCallerRoleKeyAsync resolves the caller's role_key —
    /// exactly the same lookup /auth/me uses (see StaffRoleMapper.ToRoleKey).
    private static async Task SeedStaffLinkAsync(SqlServerFixture fx, Guid tenantId, Guid userId, string designation)
    {
        await using var conn = new Microsoft.Data.SqlClient.SqlConnection(fx.ConnectionString);
        await conn.OpenAsync();
        await conn.ExecuteAsync("EXEC sp_set_session_context @key=N'TenantId', @value=@tenantId", new { tenantId });
        await conn.ExecuteAsync(
            "INSERT dbo.Staff (Id, TenantId, Name, Role, UserId) VALUES (NEWID(), @tenantId, @name, @role, @userId)",
            new { tenantId, name = $"Staff {designation}", role = designation, userId });
    }

    [Fact]
    public async Task Task_assigned_to_a_specific_user_shows_only_for_them()
    {
        var tenantId = Guid.NewGuid();
        var adminId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var otherId = Guid.NewGuid();
        await SeedUserAsync(fx, tenantId, adminId, "Admin");
        await SeedUserAsync(fx, tenantId, targetId, "Target Driver");
        await SeedUserAsync(fx, tenantId, otherId, "Other Driver");
        await SeedManagerRoleAsync(fx, adminId, Policies.SchoolAdmin);
        await SeedStaffLinkAsync(fx, tenantId, targetId, "Driver");
        await SeedStaffLinkAsync(fx, tenantId, otherId, "Driver");

        var app = App(fx);
        var adminClient = ClientFor(app, tenantId, adminId, Policies.SchoolAdmin);
        var targetClient = ClientFor(app, tenantId, targetId, "driver");
        var otherClient = ClientFor(app, tenantId, otherId, "driver");

        var create = await adminClient.PostAsJsonAsync("/v1/staff/tasks", new
        {
            title = "Wash bus #3", priority = "normal", assigned_to_user_id = targetId,
        });
        create.StatusCode.Should().Be(HttpStatusCode.Created);

        var targetList = await targetClient.GetAsync("/v1/staff/tasks");
        using var targetDoc = JsonDocument.Parse(await targetList.Content.ReadAsStringAsync());
        targetDoc.RootElement.GetProperty("data").GetArrayLength().Should().Be(1);

        var otherList = await otherClient.GetAsync("/v1/staff/tasks");
        using var otherDoc = JsonDocument.Parse(await otherList.Content.ReadAsStringAsync());
        otherDoc.RootElement.GetProperty("data").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task Task_broadcast_to_a_role_shows_for_every_user_with_that_role_and_not_others()
    {
        var tenantId = Guid.NewGuid();
        var adminId = Guid.NewGuid();
        var driver1 = Guid.NewGuid();
        var driver2 = Guid.NewGuid();
        var conductorId = Guid.NewGuid();
        await SeedUserAsync(fx, tenantId, adminId, "Admin");
        await SeedUserAsync(fx, tenantId, driver1, "Driver One");
        await SeedUserAsync(fx, tenantId, driver2, "Driver Two");
        await SeedUserAsync(fx, tenantId, conductorId, "Conductor One");
        await SeedManagerRoleAsync(fx, adminId, Policies.SchoolAdmin);
        await SeedStaffLinkAsync(fx, tenantId, driver1, "Driver");
        await SeedStaffLinkAsync(fx, tenantId, driver2, "Driver");
        await SeedStaffLinkAsync(fx, tenantId, conductorId, "Conductor");

        var app = App(fx);
        var adminClient = ClientFor(app, tenantId, adminId, Policies.SchoolAdmin);
        var driver1Client = ClientFor(app, tenantId, driver1, "driver");
        var driver2Client = ClientFor(app, tenantId, driver2, "driver");
        var conductorClient = ClientFor(app, tenantId, conductorId, "conductor");

        var create = await adminClient.PostAsJsonAsync("/v1/staff/tasks", new
        {
            title = "Morning vehicle checklist", priority = "normal", assigned_to_role_key = "driver",
        });
        create.StatusCode.Should().Be(HttpStatusCode.Created);

        foreach (var client in new[] { driver1Client, driver2Client })
        {
            var list = await client.GetAsync("/v1/staff/tasks");
            using var doc = JsonDocument.Parse(await list.Content.ReadAsStringAsync());
            doc.RootElement.GetProperty("data").GetArrayLength().Should().Be(1);
        }

        var conductorList = await conductorClient.GetAsync("/v1/staff/tasks");
        using var conductorDoc = JsonDocument.Parse(await conductorList.Content.ReadAsStringAsync());
        conductorDoc.RootElement.GetProperty("data").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task Completing_a_broadcast_task_marks_it_done_for_everyone_who_could_see_it()
    {
        var tenantId = Guid.NewGuid();
        var adminId = Guid.NewGuid();
        var driver1 = Guid.NewGuid();
        await SeedUserAsync(fx, tenantId, adminId, "Admin");
        await SeedUserAsync(fx, tenantId, driver1, "Driver One");
        await SeedManagerRoleAsync(fx, adminId, Policies.SchoolAdmin);
        await SeedStaffLinkAsync(fx, tenantId, driver1, "Driver");

        var app = App(fx);
        var adminClient = ClientFor(app, tenantId, adminId, Policies.SchoolAdmin);

        var create = await adminClient.PostAsJsonAsync("/v1/staff/tasks", new
        {
            title = "Sweep the yard", priority = "normal", assigned_to_role_key = "sweeper",
        });
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        using var doc = JsonDocument.Parse(await create.Content.ReadAsStringAsync());
        var id = doc.RootElement.GetProperty("data").GetProperty("id").GetString();

        var sweeperId = Guid.NewGuid();
        await SeedUserAsync(fx, tenantId, sweeperId, "Sweeper One");
        await SeedStaffLinkAsync(fx, tenantId, sweeperId, "Sweeper");
        var sweeperClient = ClientFor(app, tenantId, sweeperId, "sweeper");

        var complete = await sweeperClient.PostAsync($"/v1/staff/tasks/{id}/complete", null);
        complete.StatusCode.Should().Be(HttpStatusCode.OK);
        using var completeDoc = JsonDocument.Parse(await complete.Content.ReadAsStringAsync());
        var refreshed = completeDoc.RootElement.GetProperty("data").EnumerateArray().First();
        refreshed.GetProperty("done").GetBoolean().Should().BeTrue();

        var driver1Client = ClientFor(app, tenantId, driver1, "driver");
        var driverList = await driver1Client.GetAsync("/v1/staff/tasks");
        using var driverDoc = JsonDocument.Parse(await driverList.Content.ReadAsStringAsync());
        driverDoc.RootElement.GetProperty("data").GetArrayLength().Should()
            .Be(0, "the sweeper-only broadcast was never visible to a driver");
    }

    [Fact]
    public async Task Completing_someone_elses_specifically_assigned_task_is_forbidden()
    {
        var tenantId = Guid.NewGuid();
        var adminId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var otherId = Guid.NewGuid();
        await SeedUserAsync(fx, tenantId, adminId, "Admin");
        await SeedUserAsync(fx, tenantId, targetId, "Target");
        await SeedUserAsync(fx, tenantId, otherId, "Other");
        await SeedManagerRoleAsync(fx, adminId, Policies.SchoolAdmin);

        var app = App(fx);
        var adminClient = ClientFor(app, tenantId, adminId, Policies.SchoolAdmin);
        var otherClient = ClientFor(app, tenantId, otherId, "driver");

        var create = await adminClient.PostAsJsonAsync("/v1/staff/tasks", new
        {
            title = "Fix the gate", priority = "urgent", assigned_to_user_id = targetId,
        });
        using var doc = JsonDocument.Parse(await create.Content.ReadAsStringAsync());
        var id = doc.RootElement.GetProperty("data").GetProperty("id").GetString();

        var complete = await otherClient.PostAsync($"/v1/staff/tasks/{id}/complete", null);
        complete.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Completing_a_task_across_tenants_is_forbidden()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var adminA = Guid.NewGuid();
        var targetA = Guid.NewGuid();
        var userB = Guid.NewGuid();
        await SeedUserAsync(fx, tenantA, adminA, "Admin A");
        await SeedUserAsync(fx, tenantA, targetA, "Target A");
        await SeedUserAsync(fx, tenantB, userB, "User B");
        await SeedManagerRoleAsync(fx, adminA, Policies.SchoolAdmin);

        var app = App(fx);
        var adminAClient = ClientFor(app, tenantA, adminA, Policies.SchoolAdmin);
        var clientB = ClientFor(app, tenantB, userB, "driver");

        var create = await adminAClient.PostAsJsonAsync("/v1/staff/tasks", new
        {
            title = "Tenant A task", priority = "normal", assigned_to_user_id = targetA,
        });
        using var doc = JsonDocument.Parse(await create.Content.ReadAsStringAsync());
        var id = doc.RootElement.GetProperty("data").GetProperty("id").GetString();

        // RLS hides tenant A's row from tenant B's session, so the "not your task" 403 path
        // never even runs — it resolves as the same not_found a truly-missing id would.
        var complete = await clientB.PostAsync($"/v1/staff/tasks/{id}/complete", null);
        complete.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Non_manager_cannot_create_a_task()
    {
        var tenantId = Guid.NewGuid();
        var driverId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        await SeedUserAsync(fx, tenantId, driverId, "Driver");
        await SeedUserAsync(fx, tenantId, targetId, "Target");
        var client = ClientFor(App(fx), tenantId, driverId, "driver");

        var create = await client.PostAsJsonAsync("/v1/staff/tasks", new
        {
            title = "Attempted task", priority = "normal", assigned_to_user_id = targetId,
        });
        create.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Non_manager_cannot_list_all_tasks()
    {
        var tenantId = Guid.NewGuid();
        var driverId = Guid.NewGuid();
        await SeedUserAsync(fx, tenantId, driverId, "Driver");
        var client = ClientFor(App(fx), tenantId, driverId, "driver");

        var list = await client.GetAsync("/v1/staff/tasks/all");
        list.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Manager_can_list_all_tasks_in_their_tenant()
    {
        var tenantId = Guid.NewGuid();
        var adminId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        await SeedUserAsync(fx, tenantId, adminId, "Admin");
        await SeedUserAsync(fx, tenantId, targetId, "Target");
        await SeedManagerRoleAsync(fx, adminId, Policies.SchoolAdmin);
        var client = ClientFor(App(fx), tenantId, adminId, Policies.SchoolAdmin);

        await client.PostAsJsonAsync("/v1/staff/tasks", new
        {
            title = "Task one", priority = "normal", assigned_to_user_id = targetId,
        });
        await client.PostAsJsonAsync("/v1/staff/tasks", new
        {
            title = "Task two", priority = "urgent", assigned_to_role_key = "guard",
        });

        var list = await client.GetAsync("/v1/staff/tasks/all");
        list.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await list.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("data").GetArrayLength().Should().Be(2);
    }

    [Fact]
    public async Task Cross_tenant_tasks_are_never_visible_in_list_all()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var adminA = Guid.NewGuid();
        var adminB = Guid.NewGuid();
        var targetA = Guid.NewGuid();
        await SeedUserAsync(fx, tenantA, adminA, "Admin A");
        await SeedUserAsync(fx, tenantA, targetA, "Target A");
        await SeedUserAsync(fx, tenantB, adminB, "Admin B");
        await SeedManagerRoleAsync(fx, adminA, Policies.SchoolAdmin);
        await SeedManagerRoleAsync(fx, adminB, Policies.SchoolAdmin);

        var app = App(fx);
        var adminAClient = ClientFor(app, tenantA, adminA, Policies.SchoolAdmin);
        var adminBClient = ClientFor(app, tenantB, adminB, Policies.SchoolAdmin);

        await adminAClient.PostAsJsonAsync("/v1/staff/tasks", new
        {
            title = "Tenant A task", priority = "normal", assigned_to_user_id = targetA,
        });

        var listB = await adminBClient.GetAsync("/v1/staff/tasks/all");
        using var docB = JsonDocument.Parse(await listB.Content.ReadAsStringAsync());
        docB.RootElement.GetProperty("data").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task Cross_tenant_tasks_are_never_visible_in_my_tasks()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var adminA = Guid.NewGuid();
        var userB = Guid.NewGuid();
        await SeedUserAsync(fx, tenantA, adminA, "Admin A");
        await SeedUserAsync(fx, tenantB, userB, "User B");
        await SeedManagerRoleAsync(fx, adminA, Policies.SchoolAdmin);

        var app = App(fx);
        var adminAClient = ClientFor(app, tenantA, adminA, Policies.SchoolAdmin);
        var clientB = ClientFor(app, tenantB, userB, "driver");

        await adminAClient.PostAsJsonAsync("/v1/staff/tasks", new
        {
            title = "Tenant A task", priority = "normal", assigned_to_user_id = userB,
        });

        var listB = await clientB.GetAsync("/v1/staff/tasks");
        using var docB = JsonDocument.Parse(await listB.Content.ReadAsStringAsync());
        docB.RootElement.GetProperty("data").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task Create_rejects_when_neither_or_both_assignment_targets_are_set()
    {
        var tenantId = Guid.NewGuid();
        var adminId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        await SeedUserAsync(fx, tenantId, adminId, "Admin");
        var client = ClientFor(App(fx), tenantId, adminId, Policies.SchoolAdmin);

        var neither = await client.PostAsJsonAsync("/v1/staff/tasks", new { title = "No target", priority = "normal" });
        neither.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var both = await client.PostAsJsonAsync("/v1/staff/tasks", new
        {
            title = "Both targets", priority = "normal", assigned_to_user_id = targetId, assigned_to_role_key = "driver",
        });
        both.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Attach_photo_updates_photo_url_and_is_authorization_scoped_like_complete()
    {
        var tenantId = Guid.NewGuid();
        var adminId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var otherId = Guid.NewGuid();
        await SeedUserAsync(fx, tenantId, adminId, "Admin");
        await SeedUserAsync(fx, tenantId, targetId, "Target");
        await SeedUserAsync(fx, tenantId, otherId, "Other");
        await SeedManagerRoleAsync(fx, adminId, Policies.SchoolAdmin);

        var app = App(fx);
        var adminClient = ClientFor(app, tenantId, adminId, Policies.SchoolAdmin);
        var targetClient = ClientFor(app, tenantId, targetId, "driver");
        var otherClient = ClientFor(app, tenantId, otherId, "driver");

        var create = await adminClient.PostAsJsonAsync("/v1/staff/tasks", new
        {
            title = "Photo evidence", priority = "normal", assigned_to_user_id = targetId,
        });
        using var doc = JsonDocument.Parse(await create.Content.ReadAsStringAsync());
        var id = doc.RootElement.GetProperty("data").GetProperty("id").GetString();

        var forbidden = await otherClient.PostAsJsonAsync($"/v1/staff/tasks/{id}/photo",
            new { photo_base64 = "data:image/png;base64,iVBORw0KGgo=" });
        forbidden.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var ok = await targetClient.PostAsJsonAsync($"/v1/staff/tasks/{id}/photo",
            new { photo_base64 = "data:image/png;base64,iVBORw0KGgo=" });
        ok.StatusCode.Should().Be(HttpStatusCode.OK);
        using var okDoc = JsonDocument.Parse(await ok.Content.ReadAsStringAsync());
        var refreshed = okDoc.RootElement.GetProperty("data").EnumerateArray().First();
        refreshed.GetProperty("photo_url").GetString().Should().Be("data:image/png;base64,iVBORw0KGgo=");
    }
}
