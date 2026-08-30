using Sms.Application.Services.Academics;
using Sms.Application.Services.Sis;
using Sms.Modules.Academics.Data;
using Sms.Modules.Staffing.Data;
using Sms.Shared.Kernel.Auth;
using Sms.Shared.Kernel.Tenancy;

namespace Sms.Application.Services.AiSearch;

public sealed record PersonMatch(Guid Id, string Name, string Type, string? Detail);

public interface IPersonResolver
{
    Task<IReadOnlyList<PersonMatch>> ResolveAsync(
        string name, AiAuthorizationResult auth, CancellationToken ct = default);

    /// Re-authorization primitive for conversation follow-ups (Task 12): is this ALREADY-resolved
    /// student still inside a teacher's CURRENT class assignments? Reuses the exact same
    /// Grade+Section-via-ClassRepository membership check ResolveForTeacherAsync applies to a fresh
    /// name search, so a follow-up can never be less strict than an original lookup would have been.
    Task<bool> IsStillInTeacherScopeAsync(
        Guid studentId, IReadOnlyList<string> allowedClassNames, CancellationToken ct = default);
}

/// <summary>
/// Fans out across the four person-data sources this codebase has, each query independently scoped
/// by the ALREADY-authorized <see cref="AiAuthorizationResult"/> -- never a fresh, unscoped search.
/// See AiSearchAuthorizationService's doc comments for the Unrestricted/null/empty-list invariant
/// every branch below must honor exactly like every other AiSearch handler already does.
/// </summary>
public sealed class PersonResolver(
    ISisService sis, TeacherRepository teachers, StaffRepository staff,
    IUserDirectoryLookup users, ClassRepository classes, ITenantContext tenant) : IPersonResolver
{
    public async Task<IReadOnlyList<PersonMatch>> ResolveAsync(
        string name, AiAuthorizationResult auth, CancellationToken ct = default)
    {
        var trimmed = name?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed)) return [];

        if (!auth.Unrestricted)
        {
            if (auth.AllowedChildStudentIds is not null)
                return await ResolveForParentAsync(auth.AllowedChildStudentIds, trimmed, ct);
            if (auth.AllowedClassNames is not null)
                return await ResolveForTeacherAsync(auth.AllowedClassNames, trimmed, ct);
            return [];
        }

        return await ResolveUnrestrictedAsync(trimmed, ct);
    }

    public async Task<bool> IsStillInTeacherScopeAsync(
        Guid studentId, IReadOnlyList<string> allowedClassNames, CancellationToken ct = default)
    {
        var student = await sis.GetStudentAsync(studentId, ct);
        if (!student.IsSuccess) return false;

        var teacherClasses = tenant.UserId is { } teacherUserId
            ? await classes.ListForTeacherAsync(teacherUserId, ct)
            : [];
        var authorizedClasses = teacherClasses
            .Where(c => allowedClassNames.Any(cn => string.Equals(c.Name?.Trim(), cn?.Trim(), StringComparison.OrdinalIgnoreCase)))
            .ToList();

        return authorizedClasses.Any(c =>
            StudentClassScope.ClassMatches(c, student.Data!.Grade, student.Data!.Section, student.Data!.ClassLabel));
    }

    private async Task<IReadOnlyList<PersonMatch>> ResolveForParentAsync(
        IReadOnlyList<Guid> allowedChildIds, string name, CancellationToken ct)
    {
        var children = await sis.ListMyChildrenAsync(ct);
        if (!children.IsSuccess) return [];

        return children.Data!
            .Where(c => allowedChildIds.Contains(c.Id) && c.Name.Contains(name, StringComparison.OrdinalIgnoreCase))
            .Select(c => new PersonMatch(c.Id, c.Name, "student", c.ClassLabel))
            .ToList();
    }

    private async Task<IReadOnlyList<PersonMatch>> ResolveForTeacherAsync(
        IReadOnlyList<string> allowedClassNames, string name, CancellationToken ct)
    {
        var result = await sis.ListStudentsAsync(name, null, null, null, ct: ct);
        if (!result.IsSuccess) return [];

        var teacherClasses = tenant.UserId is { } teacherUserId
            ? await classes.ListForTeacherAsync(teacherUserId, ct)
            : [];
        var authorizedClasses = teacherClasses
            .Where(c => allowedClassNames.Any(cn => string.Equals(c.Name?.Trim(), cn?.Trim(), StringComparison.OrdinalIgnoreCase)))
            .ToList();

        return result.Data!.Data
            .Where(s => s.Name.Contains(name, StringComparison.OrdinalIgnoreCase)
                && authorizedClasses.Any(c => StudentClassScope.ClassMatches(c, s.Grade, s.Section, s.ClassLabel)))
            .Select(s => new PersonMatch(s.Id, s.Name, "student", s.ClassLabel))
            .ToList();
    }

    private async Task<IReadOnlyList<PersonMatch>> ResolveUnrestrictedAsync(string name, CancellationToken ct)
    {
        var matches = new List<PersonMatch>();

        var students = await sis.ListStudentsAsync(name, null, null, null, ct: ct);
        if (students.IsSuccess)
            matches.AddRange(students.Data!.Data
                .Where(s => s.Name.Contains(name, StringComparison.OrdinalIgnoreCase))
                .Select(s => new PersonMatch(s.Id, s.Name, "student", s.ClassLabel)));

        var teacherRows = await teachers.ListAsync(name, null, null, ct);
        matches.AddRange(teacherRows
            .Where(t => t.Name.Contains(name, StringComparison.OrdinalIgnoreCase))
            .Select(t => new PersonMatch(t.Id, t.Name, "teacher", t.Department)));

        var staffRows = await staff.ListAsync(name, null, ct);
        matches.AddRange(staffRows
            .Where(s => s.Name.Contains(name, StringComparison.OrdinalIgnoreCase))
            .Select(s => new PersonMatch(s.Id, s.Name, "staff", s.Department)));

        var userMatches = await users.SearchByNameAsync(name, ct);
        matches.AddRange(ResolveAdminDetails(userMatches));

        return matches;
    }

    /// Two matches sharing both Name AND Type (rare -- e.g. two Owners named "Rahul Sharma") get a
    /// masked-email tie-breaker in Detail instead of the plain role label. A single, unambiguous
    /// match for its name+type just gets the role label.
    private static IEnumerable<PersonMatch> ResolveAdminDetails(IReadOnlyList<UserDirectoryMatch> raw)
    {
        var groups = raw.GroupBy(r => (Name: r.Name.Trim().ToLowerInvariant(), r.Type));
        foreach (var group in groups)
        {
            var list = group.ToList();
            var ambiguous = list.Count > 1;
            foreach (var r in list)
            {
                var detail = ambiguous ? $"{RoleLabel(r.Type)} ({MaskEmail(r.Email)})" : RoleLabel(r.Type);
                yield return new PersonMatch(r.Id, r.Name, r.Type, detail);
            }
        }
    }

    private static string RoleLabel(string type) => type switch
    {
        "owner" => "Owner",
        "principal" => "Principal",
        _ => "Admin",
    };

    private static string MaskEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) return "";
        var at = email.IndexOf('@');
        return at <= 1 ? email : $"{email[0]}{new string('*', at - 1)}{email[at..]}";
    }
}
