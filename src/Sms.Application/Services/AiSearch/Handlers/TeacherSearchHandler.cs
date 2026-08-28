using Sms.Modules.Staffing.Data;

namespace Sms.Application.Services.AiSearch.Handlers;

public sealed class TeacherSearchHandler(
    TeacherRepository teachers, IAiAnswerTemplateService templates) : IAiIntentHandler
{
    public string Intent => "TeacherSearch";

    public async Task<AiSearchResponse> HandleAsync(
        AiAuthorizationResult auth, string language, int page, int pageSize, CancellationToken ct = default)
    {
        // Only admin/principal reach this handler per AiIntentAccessRules, so no further per-record clamp is needed.
        var rows = await teachers.ListAsync(auth.ClampedFilters.StudentName, null, null, ct);
        var clampedPageSize = Math.Clamp(pageSize, 1, 100);
        var paged = rows.Skip((page - 1) * clampedPageSize).Take(clampedPageSize).ToList();
        var answer = paged.Count == 0
            ? templates.RenderNoMatch(language)
            : $"Found {rows.Count} teacher(s) matching \"{auth.ClampedFilters.StudentName}\".";
        return AiSearchResponse.Ok(language, Intent, answer, paged, page, clampedPageSize, rows.Count, rows.Count > page * clampedPageSize);
    }
}

public sealed class StaffSearchHandler(
    StaffRepository staff, IAiAnswerTemplateService templates) : IAiIntentHandler
{
    public string Intent => "StaffSearch";

    public async Task<AiSearchResponse> HandleAsync(
        AiAuthorizationResult auth, string language, int page, int pageSize, CancellationToken ct = default)
    {
        var rows = await staff.ListAsync(auth.ClampedFilters.StudentName, null, ct);
        var clampedPageSize = Math.Clamp(pageSize, 1, 100);
        var paged = rows.Skip((page - 1) * clampedPageSize).Take(clampedPageSize).ToList();
        var answer = paged.Count == 0
            ? templates.RenderNoMatch(language)
            : $"Found {rows.Count} staff member(s) matching \"{auth.ClampedFilters.StudentName}\".";
        return AiSearchResponse.Ok(language, Intent, answer, paged, page, clampedPageSize, rows.Count, rows.Count > page * clampedPageSize);
    }
}
