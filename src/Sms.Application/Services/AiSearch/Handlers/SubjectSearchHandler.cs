using Sms.Application.Services.Academics;
using Sms.Application.Services.Sis;

namespace Sms.Application.Services.AiSearch.Handlers;

/// <summary>
/// Subjects for a single resolved student's class (self-referential "my subjects" or a parent's
/// single-name-matched child — see <see cref="AiSearchAuthorizationService"/>). Gated purely on
/// <see cref="AiAuthorizationResult.ResolvedStudentId"/>; a caller without a resolved student (e.g. a
/// teacher's generic "subjects" ask) has no class-level browse in this MVP and gets "Unsupported".
/// </summary>
public sealed class SubjectSearchHandler(
    IAcademicsService academics, ISisService sis, IAiAnswerTemplateService templates) : IAiIntentHandler
{
    public string Intent => "SubjectSearch";

    public async Task<AiSearchResponse> HandleAsync(
        AiAuthorizationResult auth, string language, int page, int pageSize, CancellationToken ct = default)
    {
        if (auth.ResolvedStudentId is not { } studentId)
            return AiSearchResponse.Terminal(language, "Unsupported", templates.RenderNoMatch(language));

        var student = await sis.GetStudentAsync(studentId, ct);
        if (!student.IsSuccess)
            return AiSearchResponse.Terminal(language, "Unsupported", templates.RenderNoMatch(language));

        var subjects = await academics.ListSubjectsForStudentAsync(
            student.Data!.Grade, student.Data.Section, student.Data.ClassLabel, ct);
        if (!subjects.IsSuccess)
            return AiSearchResponse.Terminal(language, "Forbidden", templates.RenderForbidden(language));

        var rows = subjects.Data!;
        var answer = rows.Count == 0
            ? templates.RenderNoMatch(language)
            : $"Found {rows.Count} subject(s) for {student.Data.ClassLabel}.";
        return AiSearchResponse.Ok(language, Intent, answer, rows, 1, pageSize, rows.Count, false);
    }
}
