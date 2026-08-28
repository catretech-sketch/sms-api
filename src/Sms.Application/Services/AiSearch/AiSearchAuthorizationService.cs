using Sms.Application.Services.Sis;
using Sms.Modules.Academics.Data;
using Sms.Shared.Kernel.Tenancy;

namespace Sms.Application.Services.AiSearch;

/// <summary>
/// Outcome of the single authorization choke point. Every scope value here is re-derived from
/// the authenticated caller's identity (<see cref="ITenantContext"/> / <see cref="ISisService"/>),
/// never from the LLM-extracted filters — <c>ClampedFilters</c> is the caller-safe subset of the
/// requested filters, with anything the caller is not authorized for removed.
/// </summary>
/// <param name="Allowed">False when the caller may not run this intent at all; handlers must not query.</param>
/// <param name="ResultIntent">The intent to handle, or <c>"Forbidden"</c> when <paramref name="Allowed"/> is false.</param>
/// <param name="ResolvedStudentId">
/// A single student the query was narrowed to, re-derived from the caller's own links. Null means
/// "not narrowed to one student" — see <paramref name="NameUnmatched"/> to tell "no name asked"
/// apart from "the name asked matched nothing the caller may see".
/// </param>
/// <param name="AllowedChildStudentIds">
/// The exhaustive set of student ids the caller may see, when a per-student clamp applies (parent path).
/// <para>
/// IMPORTANT — <c>null</c> here does NOT mean "no filter". Only <paramref name="Unrestricted"/> being
/// <c>true</c> means the caller has whole-tenant scope. An empty list means the caller has ZERO
/// authorized students and must see NOTHING (e.g. a parent with no <c>ParentStudentLinks</c> rows).
/// Never write <c>if (AllowedChildStudentIds is null or { Count: 0 })</c> to mean "unfiltered" — that
/// turns a zero-scope caller into a whole-tenant read. Gate on <paramref name="Unrestricted"/> first.
/// </para>
/// </param>
/// <param name="AllowedClassNames">
/// The exhaustive set of class names the caller may see, when a per-class clamp applies (teacher path).
/// <para>
/// IMPORTANT — same rule as <paramref name="AllowedChildStudentIds"/>: <c>null</c> with
/// <paramref name="Unrestricted"/> <c>false</c> is NOT "no filter", and an empty list means the caller
/// teaches nothing and must see NOTHING (e.g. a <c>school.teacher</c> JWT with no matching
/// <c>dbo.Teachers</c> row). Gate on <paramref name="Unrestricted"/>, never on emptiness.
/// </para>
/// </param>
/// <param name="ClampedFilters">The caller-safe subset of the LLM-extracted filters.</param>
/// <param name="Unrestricted">
/// True ONLY for the admin/owner/principal/staff path, where no per-record clamp applies beyond the
/// role gate and handlers may query the whole tenant. This is the single authoritative signal for
/// "no clamp"; the clamp lists' nullness is not.
/// </param>
/// <param name="NameUnmatched">
/// True only when a student name WAS asked for but resolved to nothing the caller is authorized to see
/// (e.g. a parent asking about a child that is not linked to them). False when no name was asked at all.
/// Lets a handler answer "I couldn't find a child named X" instead of silently listing everything.
/// </param>
public sealed record AiAuthorizationResult(
    bool Allowed,
    string ResultIntent,
    Guid? ResolvedStudentId,
    IReadOnlyList<Guid>? AllowedChildStudentIds,
    IReadOnlyList<string>? AllowedClassNames,
    AiSearchFilters ClampedFilters,
    bool Unrestricted,
    bool NameUnmatched);

public interface IAiSearchAuthorizationService
{
    Task<AiAuthorizationResult> AuthorizeAsync(
        string intent, AiSearchFilters filters, IReadOnlyList<string> callerRoles, CancellationToken ct = default);
}

public sealed class AiSearchAuthorizationService(
    ISisService sis, TimetableRepository timetable, ITenantContext tenant) : IAiSearchAuthorizationService
{
    private static readonly string[] TeacherRoles = ["school.teacher"];
    private static readonly string[] ParentRoles = ["student.parent"];
    private static readonly string[] AdminLikeRoles = ["school.admin", "school.owner", "school.principal"];

    public async Task<AiAuthorizationResult> AuthorizeAsync(
        string intent, AiSearchFilters filters, IReadOnlyList<string> callerRoles, CancellationToken ct = default)
    {
        if (!AiIntentAccessRules.IsAllowed(intent, callerRoles))
            return Denied("Forbidden", filters);

        var isParent = callerRoles.Any(r => ParentRoles.Contains(r, StringComparer.OrdinalIgnoreCase));
        var isTeacher = callerRoles.Any(r => TeacherRoles.Contains(r, StringComparer.OrdinalIgnoreCase));
        var isAdminLike = callerRoles.Any(r => AdminLikeRoles.Contains(r, StringComparer.OrdinalIgnoreCase));

        // Self-referential ("my attendance") always wins over any LLM-extracted student name.
        if (filters.TargetSelf)
        {
            var me = await sis.GetMyStudentAsync(ct);
            if (!me.IsSuccess)
                return Denied("Forbidden", filters);
            // Clamped to the caller's own record: emphatically NOT unrestricted.
            return Allowed(intent, me.Data!.Id, null, null, filters with { StudentName = null });
        }

        if (isParent)
        {
            var children = await sis.ListMyChildrenAsync(ct);
            var childIds = children.IsSuccess
                ? children.Data!.Select(c => c.Id).ToList()
                : [];

            // An empty childIds list is a real answer ("this parent may see nothing"), never "no filter".
            if (string.IsNullOrWhiteSpace(filters.StudentName))
                return Allowed(intent, null, childIds, null, filters);

            var match = children.IsSuccess
                ? children.Data!.FirstOrDefault(c =>
                    c.Name.Contains(filters.StudentName, StringComparison.OrdinalIgnoreCase))
                : null;
            return match is null
                // A name was asked for and matched none of the caller's children: report it as
                // unmatched rather than falling back to "show everything they may see".
                ? Allowed(intent, null, childIds, null, filters with { StudentName = null }, nameUnmatched: true)
                : Allowed(intent, match.Id, childIds, null, filters);
        }

        if (isTeacher && !isAdminLike)
        {
            if (tenant.UserId is not { } teacherUserId)
                return Denied("Forbidden", filters);

            var slots = await timetable.ListForTeacherAsync(teacherUserId, ct);
            var allowedClassNames = slots
                .Select(s => s.ClassName)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Select(n => n!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var clamped = filters;
            if (!string.IsNullOrWhiteSpace(filters.ClassName) &&
                !allowedClassNames.Contains(filters.ClassName, StringComparer.OrdinalIgnoreCase))
            {
                clamped = filters with { ClassName = null, Section = null }; // asked about a class they don't teach
            }

            // An empty allowedClassNames list is a real answer ("this teacher may see nothing" — e.g. a
            // school.teacher JWT with no matching dbo.Teachers row), never "no filter".
            return Allowed(intent, null, null, allowedClassNames, clamped);
        }

        // Admin/principal/owner/staff: no per-record clamp beyond the role gate already applied above.
        // This is the ONLY branch that may read the whole tenant, so it is the only Unrestricted = true.
        return Allowed(intent, null, null, null, filters, unrestricted: true);
    }

    private static AiAuthorizationResult Denied(string resultIntent, AiSearchFilters filters) =>
        new(false, resultIntent, null, null, null, filters, Unrestricted: false, NameUnmatched: false);

    private static AiAuthorizationResult Allowed(
        string intent, Guid? studentId, IReadOnlyList<Guid>? childIds,
        IReadOnlyList<string>? classNames, AiSearchFilters filters,
        bool unrestricted = false, bool nameUnmatched = false) =>
        new(true, intent, studentId, childIds, classNames, filters, unrestricted, nameUnmatched);
}
