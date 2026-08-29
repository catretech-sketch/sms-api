using Sms.Modules.Academics.Data;

namespace Sms.Application.Services.AiSearch.Handlers;

/// <summary>
/// Homework for a single resolved student (self-referential "my homework" or a parent's
/// single-name-matched child — see <see cref="AiSearchAuthorizationService"/>). A teacher asking a
/// generic "homework" question without a resolved student has no per-class homework browse in this
/// MVP (class-level <c>AssignmentRepository</c> browsing for teachers is deferred), so it gets
/// "Unsupported" rather than silently returning nothing scoped.
/// </summary>
public sealed class HomeworkSearchHandler(
    HomeworkRepository homework, IAiAnswerTemplateService templates) : IAiIntentHandler
{
    public string Intent => "HomeworkSearch";

    public async Task<AiSearchResponse> HandleAsync(
        AiAuthorizationResult auth, string language, int page, int pageSize, CancellationToken ct = default)
    {
        if (auth.ResolvedStudentId is not { } studentId)
            return AiSearchResponse.Terminal(language, "Unsupported", templates.RenderNoMatch(language));

        var rows = await homework.ListAsync(studentId, null, ct);
        var clampedPageSize = Math.Clamp(pageSize, 1, 100);
        var paged = rows.Skip((page - 1) * clampedPageSize).Take(clampedPageSize).ToList();
        var answer = paged.Count == 0
            ? templates.RenderNoMatch(language)
            : $"Found {rows.Count} homework item(s).";
        return AiSearchResponse.Ok(language, Intent, answer, paged, page, clampedPageSize, rows.Count, rows.Count > page * clampedPageSize);
    }
}
