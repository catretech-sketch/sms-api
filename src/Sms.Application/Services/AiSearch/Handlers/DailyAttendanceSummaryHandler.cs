using Sms.Application.Services.Academics;
using Sms.Modules.AiSearch.Data;
using Sms.Shared.Kernel.Tenancy;

namespace Sms.Application.Services.AiSearch.Handlers;

public sealed class DailyAttendanceSummaryHandler(
    AiAttendanceAggregateRepository repo, IAiAnswerTemplateService templates,
    ITenantContext tenant, TimeProvider clock) : IAiIntentHandler
{
    public string Intent => "DailyAttendanceSummary";

    public async Task<AiSearchResponse> HandleAsync(
        AiAuthorizationResult auth, string language, int page, int pageSize, CancellationToken ct = default)
    {
        if (tenant.TenantId is not { } tenantId)
            return AiSearchResponse.Fail("InvalidRequest", "missing tenant context");

        var (from, _) = DateExpressionResolver.Resolve(auth.ClampedFilters.DateExpression,
            DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime));

        // Unrestricted (admin/owner/principal/staff) sees the whole tenant. A teacher (never
        // Unrestricted) is clamped to their own class(es) — AllowedClassNames being null/empty here
        // means "teaches nothing", not "no filter", so it must never fall through to school-wide.
        //
        // AllowedClassNames[0] is free text sourced from dbo.Classes.Name (e.g. "Class 8A"), while
        // production Students.ClassLabel is generated as Grade + '-' + Section (e.g. "8-A"). Resolve
        // the free-text name against the tenant's real ClassLabel values the same way
        // ClassAttendanceHandler does, so a mismatch like "8A" vs "8-A" doesn't silently zero out a
        // teacher's own daily attendance summary. Falls back to the raw name when nothing resolves.
        var agg = auth.Unrestricted
            ? await repo.SchoolWideAsync(tenantId, from, ct)
            : auth.AllowedClassNames is { Count: > 0 } classes
                ? await repo.ForClassAsync(tenantId, await ResolveClassLabelAsync(tenantId, classes[0], ct), null, from, ct)
                : new AttendanceAggregate(0, 0, 0, 0);

        var answer = templates.RenderDailyAttendanceSummary(language, agg.Total, agg.Present, agg.Absent, agg.Pct);
        var data = new { totalStudents = agg.Total, present = agg.Present, absent = agg.Absent, attendancePercentage = agg.Pct };
        return AiSearchResponse.Ok(language, Intent, answer, data, 1, pageSize, 1, false);
    }

    private async Task<string> ResolveClassLabelAsync(Guid tenantId, string className, CancellationToken ct)
    {
        var candidateLabels = await repo.DistinctClassLabelsAsync(tenantId, ct);
        return candidateLabels.FirstOrDefault(l => StudentClassScope.LabelsMatch(l, className)) ?? className;
    }
}

public sealed class DashboardSummaryHandler(
    AiAttendanceAggregateRepository repo, IAiAnswerTemplateService templates,
    ITenantContext tenant, TimeProvider clock) : IAiIntentHandler
{
    public string Intent => "DashboardSummary";

    public async Task<AiSearchResponse> HandleAsync(
        AiAuthorizationResult auth, string language, int page, int pageSize, CancellationToken ct = default)
    {
        if (tenant.TenantId is not { } tenantId)
            return AiSearchResponse.Fail("InvalidRequest", "missing tenant context");

        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);

        // DashboardSummary is only reachable by the Unrestricted (admin-like) path per
        // AiIntentAccessRules; still gate on Unrestricted rather than assuming, so a caller who
        // somehow reaches here without whole-tenant scope gets zeros instead of a leaked read.
        var students = auth.Unrestricted
            ? await repo.SchoolWideAsync(tenantId, today, ct)
            : new AttendanceAggregate(0, 0, 0, 0);

        // Teacher/staff school-wide counts are deferred — see Task 11 for the self-scoped equivalents;
        // a true school-wide teacher/staff headcount aggregate is a follow-up once that repository exists.
        var answer = templates.RenderDailyAttendanceSummary(language, students.Total, students.Present, students.Absent, students.Pct);
        var data = new { students = new { total = students.Total, present = students.Present, absent = students.Absent, attendancePercentage = students.Pct } };
        return AiSearchResponse.Ok(language, Intent, answer, data, 1, pageSize, 1, false);
    }
}
