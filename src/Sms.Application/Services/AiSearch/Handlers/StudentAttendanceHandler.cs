using Sms.Application.Services.Sis;

namespace Sms.Application.Services.AiSearch.Handlers;

/// <summary>
/// A single student's live attendance percentage. <see cref="AiAuthorizationResult.ResolvedStudentId"/>
/// is always re-derived by the authorization service from the caller's own identity/links (self,
/// or a parent's matched child) — never from the raw LLM-extracted filter — so a null here means
/// "not narrowed to one student" (no self-scope, no matched child name) and must degrade to
/// <c>Unsupported</c> rather than querying anything.
/// </summary>
public sealed class StudentAttendanceHandler(
    ISisService sis, IAiAnswerTemplateService templates) : IAiIntentHandler
{
    public string Intent => "StudentAttendance";

    public async Task<AiSearchResponse> HandleAsync(
        AiAuthorizationResult auth, string language, int page, int pageSize, CancellationToken ct = default)
    {
        if (auth.ResolvedStudentId is not { } studentId)
            return AiSearchResponse.Terminal(language, "Unsupported", templates.RenderNoMatch(language), "no_match");

        var student = await sis.GetStudentAsync(studentId, ct);
        if (!student.IsSuccess)
            return AiSearchResponse.Terminal(language, "Unsupported", templates.RenderNoMatch(language), "no_match");

        var pct = student.Data!.AttendancePct ?? 0m;
        var answer = templates.RenderStudentAttendance(language, student.Data.Name, pct);
        var data = new { studentId, name = student.Data.Name, attendancePercentage = pct };
        return AiSearchResponse.Ok(language, Intent, answer, data, 1, pageSize, 1, false);
    }
}
