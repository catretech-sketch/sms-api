using Dapper;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Sms.Application.Services.AiSearch;
using Sms.Application.Services.AiSearch.Handlers;
using Sms.Application.Services.Sis;
using Sms.Modules.Sis.Contracts;
using Sms.Shared.Kernel.Tenancy;
using Xunit;

namespace Sms.Tests.Integration.AiSearch;

/// Exercises StudentSearchHandler directly (no HTTP layer needed yet — the AI search controller
/// lands in a later task). StudentSearchHandler is the one handler that calls the unrestricted
/// ISisService.ListStudentsAsync, so the whole point of these tests is proving the gate is
/// auth.Unrestricted — never a null/emptiness check on AllowedChildStudentIds/AllowedClassNames,
/// which (per AiAuthorizationResult's docs) can legitimately be non-null/empty for a real
/// zero-or-scoped-record caller, not "no filter".
[Collection("sql")]
public class StudentSearchHandlerTests(SqlServerFixture fx)
{
    private WebApplicationFactory<Program> App() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("environment", "Production");
            b.UseSetting("ConnectionStrings:Sql", fx.ConnectionString);
            b.UseSetting("Jwt:SigningKey", "integration-test-signing-key-32-bytes-min!!");
        });

    /// Resolves a StudentSearchHandler wired to the real ISisService, with the ambient
    /// ITenantContext set exactly as the request pipeline would after JWT validation.
    private static async Task<AiSearchResponse> Handle(
        WebApplicationFactory<Program> app, Guid tenantId, AiAuthorizationResult auth, string language = "en")
    {
        using var scope = app.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContext>().Set(tenantId, Guid.NewGuid(), isPlatform: false);
        var handler = new StudentSearchHandler(
            scope.ServiceProvider.GetRequiredService<ISisService>(),
            scope.ServiceProvider.GetRequiredService<IAiAnswerTemplateService>());
        return await handler.HandleAsync(auth, language, 1, 20);
    }

    private async Task Seed(Func<SqlConnection, Task> work)
    {
        await using var conn = new SqlConnection(fx.ConnectionString);
        await conn.OpenAsync();
        await conn.ExecuteAsync("EXEC sp_set_session_context @key=N'IsPlatform', @value=1");
        await work(conn);
    }

    private static async Task InsertStudent(
        SqlConnection conn, Guid id, Guid tenantId, string name, string admissionNoPrefix) =>
        await conn.ExecuteAsync(
            """
            INSERT dbo.Students (Id, TenantId, AdmissionNo, Name, Grade, Section, ClassLabel, Status)
            VALUES (@id, @tenantId, @adm, @name, N'8', N'A', N'8A', N'active')
            """,
            new { id, tenantId, adm = $"{admissionNoPrefix}-{Guid.NewGuid():N}"[..20], name });

    private static AiAuthorizationResult ParentScopedAuth(IReadOnlyList<Guid> childIds) => new(
        Allowed: true, ResultIntent: "StudentSearch", ResolvedStudentId: null,
        AllowedChildStudentIds: childIds, AllowedClassNames: null,
        ClampedFilters: new AiSearchFilters("Aarav", null, null, null, false),
        Unrestricted: false, NameUnmatched: false);

    private static AiAuthorizationResult AdminAuth(string? studentName) => new(
        Allowed: true, ResultIntent: "StudentSearch", ResolvedStudentId: null,
        AllowedChildStudentIds: null, AllowedClassNames: null,
        ClampedFilters: new AiSearchFilters(studentName, null, null, null, false),
        Unrestricted: true, NameUnmatched: false);

    [Fact]
    public async Task Parent_scoped_auth_result_never_reaches_the_open_roster_search()
    {
        await using var app = App();
        var tenantId = Guid.NewGuid();
        var childId = Guid.NewGuid();

        // A parent auth result carries a real, non-null/non-empty AllowedChildStudentIds — the
        // "parent shape" per AiSearchAuthorizationService — but Unrestricted stays false. The handler
        // must gate on Unrestricted only and must never call the open ListStudentsAsync for this
        // caller, no matter what AllowedChildStudentIds/StudentName look like.
        var response = await Handle(app, tenantId, ParentScopedAuth([childId]));

        response.Intent.Should().Be("Unsupported");
        response.Data.Should().BeNull();
    }

    [Fact]
    public async Task Teacher_scoped_auth_result_with_zero_scope_also_never_reaches_the_open_roster_search()
    {
        await using var app = App();
        var tenantId = Guid.NewGuid();

        // A teacher with no matching dbo.Teachers row gets an empty (not null) AllowedClassNames —
        // a real zero-scope answer per AiAuthorizationResult's docs, not "no filter". Must still
        // degrade to Unsupported rather than falling through to the unrestricted search.
        var auth = new AiAuthorizationResult(
            Allowed: true, ResultIntent: "StudentSearch", ResolvedStudentId: null,
            AllowedChildStudentIds: null, AllowedClassNames: [],
            ClampedFilters: new AiSearchFilters("Aarav", null, null, null, false),
            Unrestricted: false, NameUnmatched: false);

        var response = await Handle(app, tenantId, auth);

        response.Intent.Should().Be("Unsupported");
        response.Data.Should().BeNull();
    }

    [Fact]
    public async Task Admin_search_returns_matching_students_for_their_own_tenant_only()
    {
        await using var app = App();
        var tenantIdA = Guid.NewGuid();
        var tenantIdB = Guid.NewGuid();

        await Seed(async conn =>
        {
            await InsertStudent(conn, Guid.NewGuid(), tenantIdA, "Aarav Shah", "ADM-SS1");
            await InsertStudent(conn, Guid.NewGuid(), tenantIdB, "Aarav Shah", "ADM-SS2");
        });

        var response = await Handle(app, tenantIdA, AdminAuth("Aarav"));

        response.Intent.Should().Be("StudentSearch");
        response.Data.Should().NotBeNull();

        var rows = (IReadOnlyList<StudentResponse>)response.Data!;
        rows.Should().ContainSingle();
        rows[0].TenantId.Should().Be(tenantIdA);
        rows[0].Name.Should().Be("Aarav Shah");
    }

    [Fact]
    public async Task Admin_search_with_no_matches_returns_no_match_answer_not_null_data_crash()
    {
        await using var app = App();
        var tenantId = Guid.NewGuid();

        var response = await Handle(app, tenantId, AdminAuth("NoSuchStudentXyz"));

        response.Intent.Should().Be("StudentSearch");
        response.Data.Should().NotBeNull();
        var rows = (IReadOnlyList<StudentResponse>)response.Data!;
        rows.Should().BeEmpty();

        var templates = app.Services.GetRequiredService<IAiAnswerTemplateService>();
        response.Answer.Should().Be(templates.RenderNoMatch("en"));
    }
}
