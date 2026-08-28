using Sms.Application.Services.Attendance;

namespace Sms.Application.Services.AiSearch.Handlers;

/// <summary>
/// Self-scoped only, per the MVP catalog: <see cref="IAttendanceService.GetSummaryAsync"/> always
/// resolves against the ambient <c>ITenantContext.UserId</c> (the authenticated caller), so there is
/// no student/teacher id to narrow by and <paramref name="auth"/> is unused beyond having already
/// passed <c>AiIntentAccessRules</c>. <c>TeacherAttendanceSummaryResponse</c>
/// (<see cref="Sms.Modules.Attendance.TeacherAttendanceSummaryResponse"/>) exposes
/// <c>DaysPresent</c>/<c>DaysFlagged</c>/<c>TotalHours</c> — rather than build a bespoke template
/// method for three fields, this renders a generic "ready" answer and returns the real response
/// object as-is for the caller's own UI to render, per the brief's documented fallback.
/// </summary>
public sealed class TeacherAttendanceHandler(
    IAttendanceService attendance, IAiAnswerTemplateService templates) : IAiIntentHandler
{
    public string Intent => "TeacherAttendance";

    public async Task<AiSearchResponse> HandleAsync(
        AiAuthorizationResult auth, string language, int page, int pageSize, CancellationToken ct = default)
    {
        var summary = await attendance.GetSummaryAsync(null, null, ct);
        if (!summary.IsSuccess)
            return AiSearchResponse.Terminal(language, "Forbidden", templates.RenderForbidden(language));

        var data = summary.Data;
        var answer = language switch
        {
            "hi" => "आपकी उपस्थिति सारांश तैयार है।",
            "hinglish" => "Aapka attendance summary taiyar hai.",
            _ => "Your attendance summary is ready.",
        };
        return AiSearchResponse.Ok(language, Intent, answer, data, 1, pageSize, 1, false);
    }
}

/// <summary>
/// Same self-scoped monthly summary as <see cref="TeacherAttendanceHandler"/>, kept as a distinct
/// intent (rather than folded into TeacherAttendance) so <c>AiIntentAccessRules</c> and the
/// classifier can address staff vs. teacher callers independently, per the MVP catalog.
/// </summary>
public sealed class StaffAttendanceHandler(
    IAttendanceService attendance, IAiAnswerTemplateService templates) : IAiIntentHandler
{
    public string Intent => "StaffAttendance";

    public Task<AiSearchResponse> HandleAsync(
        AiAuthorizationResult auth, string language, int page, int pageSize, CancellationToken ct = default) =>
        new TeacherAttendanceHandler(attendance, templates).HandleAsync(auth, language, page, pageSize, ct);
}
