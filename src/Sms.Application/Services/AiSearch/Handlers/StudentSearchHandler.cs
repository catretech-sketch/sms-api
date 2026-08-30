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
        //
        // ListStudentsAsync itself is unpaged (returns every matching row; NextCursor is always
        // null — see SisService), so pagination is applied here in memory, the same Skip/Take
        // pattern TeacherSearchHandler/StaffSearchHandler already use, with pageSize hard-capped at
        // 100 server-side per the plan's global constraint.
        var result = await sis.ListStudentsAsync(
            auth.ClampedFilters.StudentName, null, null, null, ct);
        if (!result.IsSuccess)
            return AiSearchResponse.Terminal(language, "Forbidden", templates.RenderForbidden(language));

        var rows = result.Data!.Data;
        var clampedPageSize = Math.Clamp(pageSize, 1, 100);
        var paged = rows.Skip((page - 1) * clampedPageSize).Take(clampedPageSize).ToList();
        var answer = paged.Count == 0
            ? templates.RenderNoMatch(language)
            : $"Found {rows.Count} student(s) matching \"{auth.ClampedFilters.StudentName}\".";
        return AiSearchResponse.Ok(
            language, Intent, answer, paged, page, clampedPageSize, rows.Count, rows.Count > page * clampedPageSize);
    }
}

/// <summary>
/// Returns full details for a single, already-resolved student. This handler never performs its
/// own scope/authorization resolution — it relies entirely on
/// <see cref="AiAuthorizationResult.ResolvedStudentId"/> having already been set (or left null) by
/// <c>AiSearchAuthorizationService</c> for this caller. A null <c>ResolvedStudentId</c> means the
/// authorization service could not (or would not) resolve a single student for this caller/query,
/// so this handler must degrade to "Unsupported" rather than guessing or falling back to any other
/// identifier.
/// </summary>
public sealed class StudentDetailsHandler(ISisService sis, IAiAnswerTemplateService templates) : IAiIntentHandler
{
    public string Intent => "StudentDetails";

    public async Task<AiSearchResponse> HandleAsync(
        AiAuthorizationResult auth, string language, int page, int pageSize, CancellationToken ct = default)
    {
        if (auth.ResolvedStudentId is not { } studentId)
            return AiSearchResponse.Terminal(language, "Unsupported", templates.RenderNoMatch(language));

        var student = await sis.GetStudentAsync(studentId, ct);
        if (!student.IsSuccess)
            return AiSearchResponse.Terminal(language, "Unsupported", templates.RenderNoMatch(language));

        var answer = $"Showing details for {student.Data!.Name}.";
        return AiSearchResponse.Ok(language, Intent, answer, student.Data, 1, pageSize, 1, false);
    }
}
