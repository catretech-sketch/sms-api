using Sms.Modules.AiSearch.Data;
using Sms.Shared.Kernel.Tenancy;

namespace Sms.Application.Services.AiSearch.Handlers;

/// <summary>
/// Attendance for a single class (optionally narrowed to a section). The class name in play is
/// always <c>auth.ClampedFilters.ClassName</c>, never the raw LLM-extracted filter — the
/// authorization service (<see cref="AiSearchAuthorizationService"/>) already strips
/// <c>ClassName</c>/<c>Section</c> back to null for a teacher who asked about a class they don't
/// teach, so a null/blank class name here means "not authorized for any class" (or "no class was
/// asked for") and must degrade to <c>Unsupported</c> rather than querying school-wide data.
/// </summary>
public sealed class ClassAttendanceHandler(
    AiAttendanceAggregateRepository repo, IAiAnswerTemplateService templates,
    ITenantContext tenant, TimeProvider clock) : IAiIntentHandler
{
    public string Intent => "ClassAttendance";

    public async Task<AiSearchResponse> HandleAsync(
        AiAuthorizationResult auth, string language, int page, int pageSize, CancellationToken ct = default)
    {
        if (tenant.TenantId is not { } tenantId)
            return AiSearchResponse.Fail("InvalidRequest", "missing tenant context");
        if (string.IsNullOrWhiteSpace(auth.ClampedFilters.ClassName))
            return AiSearchResponse.Terminal(language, "Unsupported", templates.RenderUnsupported(language));

        var (from, _) = DateExpressionResolver.Resolve(auth.ClampedFilters.DateExpression,
            DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime));
        var agg = await repo.ForClassAsync(tenantId, auth.ClampedFilters.ClassName, auth.ClampedFilters.Section, from, ct);

        var answer = templates.RenderClassAttendance(
            language, auth.ClampedFilters.ClassName, agg.Total, agg.Present, agg.Absent, agg.Pct);
        var data = new
        {
            className = auth.ClampedFilters.ClassName,
            section = auth.ClampedFilters.Section,
            total = agg.Total,
            present = agg.Present,
            absent = agg.Absent,
            attendancePercentage = agg.Pct,
        };
        return AiSearchResponse.Ok(language, Intent, answer, data, 1, pageSize, 1, false);
    }
}

/// <summary>
/// Same query as <see cref="ClassAttendanceHandler"/> with a section already present in
/// <c>ClampedFilters</c> — kept as a distinct intent (rather than folded into ClassAttendance) so
/// <c>AiIntentAccessRules</c> and the classifier can address it independently, per Task 9/10 scope.
/// </summary>
public sealed class SectionAttendanceHandler(
    AiAttendanceAggregateRepository repo, IAiAnswerTemplateService templates,
    ITenantContext tenant, TimeProvider clock) : IAiIntentHandler
{
    public string Intent => "SectionAttendance";

    public Task<AiSearchResponse> HandleAsync(
        AiAuthorizationResult auth, string language, int page, int pageSize, CancellationToken ct = default) =>
        new ClassAttendanceHandler(repo, templates, tenant, clock).HandleAsync(auth, language, page, pageSize, ct);
}
