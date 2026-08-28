using Sms.Modules.Academics.Data;
using Sms.Shared.Kernel.Tenancy;

namespace Sms.Application.Services.AiSearch.Handlers;

/// <summary>
/// Upcoming exams (today or later) for the caller's tenant. Unrestricted (admin/owner/principal)
/// sees every exam. A non-Unrestricted caller (teacher/parent) is clamped to
/// <c>auth.AllowedClassNames</c> — matched against the exam's <c>Grades</c> field — following the
/// same discipline as <see cref="ClassAttendanceHandler"/> and
/// <see cref="DailyAttendanceSummaryHandler"/>: <c>AllowedClassNames</c> being null/empty here means
/// "authorized for zero classes", not "no filter", so it must never fall through to showing every
/// exam in the tenant.
/// </summary>
public sealed class UpcomingExamSearchHandler(
    ExamRepository exams, IAiAnswerTemplateService templates, ITenantContext tenant, TimeProvider clock) : IAiIntentHandler
{
    public string Intent => "UpcomingExamSearch";

    public async Task<AiSearchResponse> HandleAsync(
        AiAuthorizationResult auth, string language, int page, int pageSize, CancellationToken ct = default)
    {
        if (tenant.TenantId is not { } tenantId)
            return AiSearchResponse.Fail("InvalidRequest", "missing tenant context");

        var today = clock.GetUtcNow().UtcDateTime.Date;
        var rows = await exams.ListUpcomingExamsAsync(tenantId, today, ct);

        var scoped = auth.Unrestricted
            ? rows
            : auth.AllowedClassNames is { Count: > 0 } classNames
                ? rows.Where(r => r.Grades is not null &&
                    classNames.Any(c => r.Grades.Contains(c, StringComparison.OrdinalIgnoreCase))).ToList()
                : [];

        var clampedPageSize = Math.Clamp(pageSize, 1, 100);
        var paged = scoped.Skip((page - 1) * clampedPageSize).Take(clampedPageSize).ToList();
        var answer = paged.Count == 0
            ? templates.RenderNoMatch(language)
            : $"Found {scoped.Count} upcoming exam(s).";
        return AiSearchResponse.Ok(language, Intent, answer, paged, page, clampedPageSize, scoped.Count, scoped.Count > page * clampedPageSize);
    }
}

public sealed class TestSearchHandler(
    ExamRepository exams, IAiAnswerTemplateService templates, ITenantContext tenant, TimeProvider clock) : IAiIntentHandler
{
    public string Intent => "TestSearch";

    public Task<AiSearchResponse> HandleAsync(
        AiAuthorizationResult auth, string language, int page, int pageSize, CancellationToken ct = default) =>
        new UpcomingExamSearchHandler(exams, templates, tenant, clock).HandleAsync(auth, language, page, pageSize, ct);
}
