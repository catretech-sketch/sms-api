using Sms.Application.Services.Sis;

namespace Sms.Application.Services.AiSearch.Handlers;

/// <summary>
/// Open free-text roster search (name/admission no/class label). This is the one AI-search handler
/// that calls the unrestricted <see cref="ISisService.ListStudentsAsync"/> — which has no per-record
/// clamp of its own — so it must never be reachable by a caller whose scope is anything less than the
/// whole tenant.
/// <para>
/// Gate on <see cref="AiAuthorizationResult.Unrestricted"/> only, exactly as
/// <c>AiSearchAuthorizationService</c> documents: <c>Unrestricted</c> is the single authoritative
/// signal for "no clamp applies" (admin/owner/principal/staff). A null/empty
/// <see cref="AiAuthorizationResult.AllowedChildStudentIds"/> or
/// <see cref="AiAuthorizationResult.AllowedClassNames"/> is NOT "no filter" — it can mean "this
/// parent/teacher may see zero records" — so checking those fields for null/emptiness here would let
/// a scoped caller (parent, teacher, student) fall through into the open roster search and see
/// students outside their authorized scope. Any non-unrestricted caller already got their own
/// resolved scope (ResolvedStudentId / AllowedChildStudentIds / AllowedClassNames) from the
/// authorization service and must degrade to "no results" instead.
/// </para>
/// </summary>
public sealed class StudentSearchHandler(ISisService sis, IAiAnswerTemplateService templates) : IAiIntentHandler
{
    public string Intent => "StudentSearch";

    public async Task<AiSearchResponse> HandleAsync(
        AiAuthorizationResult auth, string language, int page, int pageSize, CancellationToken ct = default)
    {
        if (!auth.Unrestricted)
            return AiSearchResponse.Terminal(language, "Unsupported", templates.RenderNoMatch(language));

        // ISisService.ListStudentsAsync's "grade" parameter matches dbo.Students.Grade exactly
        // (e.g. "8"), not a class label like "8A" — passing ClampedFilters.ClassName there would
        // silently over-filter to zero rows for any class-shaped query. The repository's "q" LIKE
        // already matches Name, AdmissionNo, and ClassLabel, so StudentName alone covers both a name
        // search and a class-label search; grade/status/fee are left unfiltered here.
        var result = await sis.ListStudentsAsync(
            auth.ClampedFilters.StudentName, null, null, null, ct);
        if (!result.IsSuccess)
            return AiSearchResponse.Terminal(language, "Forbidden", templates.RenderForbidden(language));

        var rows = result.Data!.Data;
        var answer = rows.Count == 0
            ? templates.RenderNoMatch(language)
            : $"Found {rows.Count} student(s) matching \"{auth.ClampedFilters.StudentName}\".";
        return AiSearchResponse.Ok(language, Intent, answer, rows, page, pageSize, rows.Count, result.Data.NextCursor is not null);
    }
}
