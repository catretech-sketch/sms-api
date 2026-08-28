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
public sealed record AiAuthorizationResult(
    bool Allowed,
    string ResultIntent,
    Guid? ResolvedStudentId,
    IReadOnlyList<Guid>? AllowedChildStudentIds,
    IReadOnlyList<string>? AllowedClassNames,
    AiSearchFilters ClampedFilters);

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
            return Allowed(intent, me.Data!.Id, null, null, filters with { StudentName = null });
        }

        if (isParent)
        {
            var children = await sis.ListMyChildrenAsync(ct);
            var childIds = children.IsSuccess
                ? children.Data!.Select(c => c.Id).ToList()
                : [];

            if (string.IsNullOrWhiteSpace(filters.StudentName))
                return Allowed(intent, null, childIds, null, filters);

            var match = children.IsSuccess
                ? children.Data!.FirstOrDefault(c =>
                    c.Name.Contains(filters.StudentName, StringComparison.OrdinalIgnoreCase))
                : null;
            return match is null
                ? Allowed(intent, null, childIds, null, filters with { StudentName = null }) // no-match, not a leak
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

            return Allowed(intent, null, null, allowedClassNames, clamped);
        }

        // Admin/principal/owner/staff: no per-record clamp beyond the role gate already applied above.
        return Allowed(intent, null, null, null, filters);
    }

    private static AiAuthorizationResult Denied(string resultIntent, AiSearchFilters filters) =>
        new(false, resultIntent, null, null, null, filters);

    private static AiAuthorizationResult Allowed(
        string intent, Guid? studentId, IReadOnlyList<Guid>? childIds,
        IReadOnlyList<string>? classNames, AiSearchFilters filters) =>
        new(true, intent, studentId, childIds, classNames, filters);
}
