using Sms.Application.Services.Academics;
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
/// <para>
/// Production <c>Students.ClassLabel</c> is generated as Grade + '-' + Section (e.g. <c>"8-A"</c>),
/// but the filter here is free text like <c>"8A"</c> (a caller's own phrasing, or a
/// <c>TimetableSlots.ClassName</c> value). Before querying, the free-text filter is resolved against
/// the tenant's real <c>ClassLabel</c> values using <see cref="StudentClassScope.LabelsMatch"/> — the
/// same normalizer already used for class-label matching elsewhere — so a realistic mismatch like
/// "8A" vs "8-A" still finds the class. When nothing resolves, the original filter is passed through
/// unchanged so the exact-match SQL still degrades to a graceful zero-count answer rather than
/// silently swallowing a genuinely unknown class name.
/// </para>
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

        var candidateLabels = await repo.DistinctClassLabelsAsync(tenantId, ct);
        var resolvedClassName = candidateLabels.FirstOrDefault(
            l => StudentClassScope.LabelsMatch(l, auth.ClampedFilters.ClassName)) ?? auth.ClampedFilters.ClassName;

        var agg = await repo.ForClassAsync(tenantId, resolvedClassName, auth.ClampedFilters.Section, from, ct);

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
