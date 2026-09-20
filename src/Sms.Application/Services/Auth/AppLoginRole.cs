namespace Sms.Application.Services.Auth;

/// Student vs parent app login. Parent accounts use role `student.parent`.
public static class AppLoginRole
{
    public static bool IsParent(IEnumerable<string> roles) =>
        roles.Any(r =>
            r.Contains("parent", StringComparison.OrdinalIgnoreCase)
            || r.Contains("guardian", StringComparison.OrdinalIgnoreCase));

    public static bool IsStudent(IEnumerable<string> roles)
    {
        var list = roles as IList<string> ?? roles.ToList();
        if (IsParent(list)) return false;
        return list.Any(r =>
            r.Equals("student", StringComparison.OrdinalIgnoreCase)
            || r.EndsWith(".student", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Empty requested role does not filter (teacher/admin/CATRE apps never send `role`).
    /// Any other requested role is the sms-staff app's duty-tab selector (driver/conductor/
    /// sweeper/gardener/guard/peon, ...) — UserRoles stores that duty category verbatim
    /// (e.g. "driver"), so the account must hold that exact role.
    /// </summary>
    public static bool Matches(IEnumerable<string> roles, string? requested)
    {
        if (string.IsNullOrWhiteSpace(requested)) return true;
        var want = requested.Trim().ToLowerInvariant();
        if (want is "student") return IsStudent(roles);
        if (want is "parent") return IsParent(roles);
        return roles.Any(r => r.Equals(want, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Message when the password matches but the requested tab/role does not.</summary>
    public static string? WrongTabMessage(IEnumerable<string> actualRoles, string? requested)
    {
        if (string.IsNullOrWhiteSpace(requested)) return null;
        var want = requested.Trim().ToLowerInvariant();
        var roles = actualRoles as IList<string> ?? actualRoles.ToList();
        if (Matches(roles, requested)) return null;
        if (want == "student" && IsParent(roles))
            return "This is a parent login. Switch to the Parent tab.";
        if (want == "parent" && IsStudent(roles))
            return "This is a student login. Switch to the Student tab.";
        if (want is not ("student" or "parent"))
        {
            if (IsParent(roles)) return "This is a parent account. Staff members only.";
            if (IsStudent(roles)) return "This is a student account. Staff members only.";
            return "This account is not registered for that role.";
        }
        return "This account cannot sign in with the selected role.";
    }
}
