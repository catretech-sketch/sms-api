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

    /// <summary>Empty or unknown requested role does not filter (staff/admin logins).</summary>
    public static bool Matches(IEnumerable<string> roles, string? requested)
    {
        if (string.IsNullOrWhiteSpace(requested)) return true;
        var want = requested.Trim().ToLowerInvariant();
        if (want is "student") return IsStudent(roles);
        if (want is "parent") return IsParent(roles);
        return true;
    }

    /// <summary>Message when the password matches but the Student/Parent tab does not.</summary>
    public static string? WrongTabMessage(IEnumerable<string> actualRoles, string? requested)
    {
        if (string.IsNullOrWhiteSpace(requested)) return null;
        var want = requested.Trim().ToLowerInvariant();
        if (want is not ("student" or "parent")) return null;
        if (Matches(actualRoles, requested)) return null;
        if (want == "student" && IsParent(actualRoles))
            return "This is a parent login. Switch to the Parent tab.";
        if (want == "parent" && IsStudent(actualRoles))
            return "This is a student login. Switch to the Student tab.";
        return "This account cannot sign in with the selected role.";
    }
}
