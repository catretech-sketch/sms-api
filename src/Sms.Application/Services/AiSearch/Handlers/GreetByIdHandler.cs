using Sms.Application.Services.Academics;
using Sms.Application.Services.Sis;
using Sms.Modules.Academics.Data;
using Sms.Modules.Staffing.Data;
using Sms.Shared.Kernel.Tenancy;
using Sms.Shared.Kernel.Time;

namespace Sms.Application.Services.AiSearch.Handlers;

/// <summary>
/// Resolves an exact admission number (student) or employee code (teacher/staff) — scanned or
/// typed — to a person's name within the caller's authorized scope, and answers with a
/// time-of-day greeting. GreetById reuses <c>ClampedFilters.StudentName</c> to carry the raw
/// scanned code (not a person's name) — see <see cref="AiSearchAuthorizationService"/>'s
/// GreetById-only bypass, which hands this handler the caller's real scope with the code intact
/// and unclamped.
/// <para>
/// This handler performs its OWN exact-id resolution within that scope — never trusting an
/// admission/employee code match alone without independently checking it against the caller's
/// authorized set:
/// </para>
/// <list type="bullet">
/// <item>Parent (<c>!auth.Unrestricted</c>, <c>AllowedChildStudentIds</c> set): the matched child's
/// admission number AND id must both check out against the caller's own children.</item>
/// <item>Teacher (<c>!auth.Unrestricted</c>, <c>AllowedClassNames</c> set): the matched student's
/// admission number must match AND their class must resolve into a class this teacher teaches.</item>
/// <item>Admin/Owner/Principal/Staff (<c>auth.Unrestricted</c>): tries an exact student admission-no
/// match first, then an exact teacher employee-code match, then an exact staff employee-code match.
/// Only this fully unrestricted path may resolve staff/teacher codes at all.</item>
/// </list>
/// Any other outcome — including a code that matches something the caller is not authorized to
/// see — is treated identically to "matches nothing at all": a clean, generic no-match response
/// that never reveals partial information.
/// </summary>
public sealed class GreetByIdHandler(
    ISisService sis, TeacherRepository teachers, StaffRepository staff,
    ClassRepository classes, ITenantContext tenant,
    IAiAnswerTemplateService templates, TimeProvider clock) : IAiIntentHandler
{
    public const string IntentName = "GreetById";

    public string Intent => IntentName;

    public async Task<AiSearchResponse> HandleAsync(
        AiAuthorizationResult auth, string language, int page, int pageSize, CancellationToken ct = default)
    {
        var code = auth.ClampedFilters.StudentName?.Trim();
        if (string.IsNullOrWhiteSpace(code))
            return NoMatch(language);

        if (!auth.Unrestricted)
        {
            // Parent path: AllowedChildStudentIds is the caller's exhaustive authorized set (never
            // null for a parent — see AiSearchAuthorizationService's GreetById bypass).
            if (auth.AllowedChildStudentIds is not null)
                return await ResolveForParentAsync(auth.AllowedChildStudentIds, code, language, pageSize, ct);

            // Teacher path: AllowedClassNames is the caller's exhaustive authorized set.
            if (auth.AllowedClassNames is not null)
                return await ResolveForTeacherAsync(auth.AllowedClassNames, code, language, pageSize, ct);

            // Neither shape populated (e.g. Forbidden already handled upstream) — never leak.
            return NoMatch(language);
        }

        return await ResolveUnrestrictedAsync(code, language, pageSize, ct);
    }

    private async Task<AiSearchResponse> ResolveForParentAsync(
        IReadOnlyList<Guid> allowedChildIds, string code, string language, int pageSize, CancellationToken ct)
    {
        var children = await sis.ListMyChildrenAsync(ct);
        if (!children.IsSuccess) return NoMatch(language);

        // Belt-and-braces: both the admission-no match AND the id membership check must hold, even
        // though both are sourced from the same ListMyChildrenAsync call in practice.
        var match = children.Data!.FirstOrDefault(c =>
            string.Equals(c.AdmissionNo?.Trim(), code, StringComparison.OrdinalIgnoreCase)
            && allowedChildIds.Contains(c.Id));

        return match is null
            ? NoMatch(language)
            : Greet(language, match.Id, match.Name, "student", pageSize);
    }

    private async Task<AiSearchResponse> ResolveForTeacherAsync(
        IReadOnlyList<string> allowedClassNames, string code, string language, int pageSize, CancellationToken ct)
    {
        var result = await sis.ListStudentsAsync(code, null, null, null, ct);
        if (!result.IsSuccess) return NoMatch(language);

        // dbo.Students.ClassLabel is always the compact "Grade-Section" shape, but a teacher's
        // authorized Classes.Name can be free text (e.g. "Section Eight A") that never compacts
        // down to match it. Re-resolve the caller's authorized class names back into their actual
        // Classes rows (with real Grade/Section columns) and reuse the same membership rule the
        // rest of the codebase already uses for this exact problem: (Grade+Section match) OR
        // (ClassLabel = Classes.Name) — see AcademicsRepositories.ListForTeacherAsync /
        // StudentClassScope.ClassMatches.
        var teacherClasses = tenant.UserId is { } teacherUserId
            ? await classes.ListForTeacherAsync(teacherUserId, ct)
            : [];
        var authorizedClasses = teacherClasses
            .Where(c => allowedClassNames.Any(cn => string.Equals(c.Name?.Trim(), cn?.Trim(), StringComparison.OrdinalIgnoreCase)))
            .ToList();

        var candidate = result.Data!.Data.FirstOrDefault(s =>
            string.Equals(s.AdmissionNo?.Trim(), code, StringComparison.OrdinalIgnoreCase)
            && authorizedClasses.Any(c => StudentClassScope.ClassMatches(c, s.Grade, s.Section, s.ClassLabel)));

        return candidate is null
            ? NoMatch(language)
            : Greet(language, candidate.Id, candidate.Name, "student", pageSize);
    }

    private async Task<AiSearchResponse> ResolveUnrestrictedAsync(
        string code, string language, int pageSize, CancellationToken ct)
    {
        // Student admission number takes priority, then teacher employee code, then staff employee
        // code — a defined, documented order in the (shouldn't-happen) case of a shared code.
        var students = await sis.ListStudentsAsync(code, null, null, null, ct);
        if (students.IsSuccess)
        {
            var studentMatch = students.Data!.Data.FirstOrDefault(
                s => string.Equals(s.AdmissionNo?.Trim(), code, StringComparison.OrdinalIgnoreCase));
            if (studentMatch is not null)
                return Greet(language, studentMatch.Id, studentMatch.Name, "student", pageSize);
        }

        var teacherRows = await teachers.ListAsync(code, null, null, ct);
        var teacherMatch = teacherRows.FirstOrDefault(
            t => string.Equals(t.EmployeeCode?.Trim(), code, StringComparison.OrdinalIgnoreCase));
        if (teacherMatch is not null)
            return Greet(language, teacherMatch.Id, teacherMatch.Name, "teacher", pageSize);

        var staffRows = await staff.ListAsync(code, null, ct);
        var staffMatch = staffRows.FirstOrDefault(
            s => string.Equals(s.EmployeeCode?.Trim(), code, StringComparison.OrdinalIgnoreCase));
        if (staffMatch is not null)
            return Greet(language, staffMatch.Id, staffMatch.Name, "staff", pageSize);

        return NoMatch(language);
    }

    private AiSearchResponse Greet(string language, Guid id, string name, string type, int pageSize)
    {
        var hour = SchoolClock.ToSchoolLocal(clock.GetUtcNow().UtcDateTime).Hour;
        var answer = templates.RenderGreeting(language, name, hour);
        var data = new { id, name, type };
        return AiSearchResponse.Ok(language, Intent, answer, data, 1, pageSize, 1, false);
    }

    private AiSearchResponse NoMatch(string language) =>
        AiSearchResponse.Terminal(language, "Unsupported", templates.RenderNoMatch(language));
}
