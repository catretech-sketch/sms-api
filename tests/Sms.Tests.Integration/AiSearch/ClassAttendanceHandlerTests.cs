using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Sms.Application.Services.AiSearch;
using Sms.Application.Services.AiSearch.Handlers;
using Sms.Modules.AiSearch.Data;
using Sms.Shared.Kernel.Tenancy;
using Xunit;

namespace Sms.Tests.Integration.AiSearch;

/// Exercises ClassAttendanceHandler's degrade-gracefully path directly (no HTTP layer needed yet —
/// the AI search controller lands in Task 12). Both tests assert the handler never queries when the
/// class name in play is missing, whether that's because the caller never asked for a class, or
/// because the authorization service already clamped a disallowed class name back to null.
[Collection("sql")]
public class ClassAttendanceHandlerTests(SqlServerFixture fx)
{
    private const string Key = "integration-test-signing-key-32-bytes-min!!";

    private WebApplicationFactory<Program> App() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("environment", "Production");
            b.UseSetting("ConnectionStrings:Sql", fx.ConnectionString);
            b.UseSetting("Jwt:SigningKey", Key);
        });

    /// Resolves a ClassAttendanceHandler wired to the real repository, with the ambient
    /// ITenantContext set exactly as the request pipeline would after JWT validation.
    private static async Task<AiSearchResponse> Handle(
        WebApplicationFactory<Program> app, Guid tenantId, AiAuthorizationResult auth)
    {
        using var scope = app.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContext>().Set(tenantId, Guid.NewGuid(), isPlatform: false);
        var handler = new ClassAttendanceHandler(
            scope.ServiceProvider.GetRequiredService<AiAttendanceAggregateRepository>(),
            scope.ServiceProvider.GetRequiredService<IAiAnswerTemplateService>(),
            scope.ServiceProvider.GetRequiredService<ITenantContext>(),
            scope.ServiceProvider.GetRequiredService<TimeProvider>());
        return await handler.HandleAsync(auth, "en", 1, 20);
    }

    [Fact]
    public async Task Missing_class_name_returns_Unsupported_instead_of_throwing()
    {
        await using var app = App();
        var tenantId = Guid.NewGuid();

        // No class name was ever asked for (e.g. classifier extracted no ClassName filter).
        var filters = new AiSearchFilters(null, null, null, "today", false);
        var auth = new AiAuthorizationResult(
            Allowed: true, ResultIntent: "ClassAttendance", ResolvedStudentId: null,
            AllowedChildStudentIds: null, AllowedClassNames: null,
            ClampedFilters: filters, Unrestricted: true, NameUnmatched: false);

        var response = await Handle(app, tenantId, auth);

        response.Intent.Should().Be("Unsupported");
        response.Data.Should().BeNull();
    }

    [Fact]
    public async Task Teachers_class_filter_that_was_clamped_to_null_by_authorization_also_returns_Unsupported()
    {
        await using var app = App();
        var tenantId = Guid.NewGuid();

        // Simulates the "teacher asked about a class they don't teach" path from Task 7 — the
        // authorization service already stripped ClassName/Section back to null, and this teacher's
        // AllowedClassNames is empty (a school.teacher JWT with no matching dbo.Teachers row): a real
        // zero-scope answer, not "no filter". The handler must still degrade gracefully, not 500.
        var filters = new AiSearchFilters(null, null, null, "today", false);
        var auth = new AiAuthorizationResult(
            Allowed: true, ResultIntent: "ClassAttendance", ResolvedStudentId: null,
            AllowedChildStudentIds: null, AllowedClassNames: [],
            ClampedFilters: filters, Unrestricted: false, NameUnmatched: false);

        var response = await Handle(app, tenantId, auth);

        response.Intent.Should().Be("Unsupported");
        response.Data.Should().BeNull();
    }
}
