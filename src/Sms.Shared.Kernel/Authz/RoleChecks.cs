using System.Security.Claims;

namespace Sms.Shared.Kernel.Authz;

public static class RoleChecks
{
    public static bool IsStaff(ClaimsPrincipal user)
    {
        foreach (var claim in user.FindAll("role"))
        {
            var role = claim.Value.ToLowerInvariant();
            if (role.Contains("admin") || role.Contains("teacher") || role.Contains("principal")
                || role.Contains("owner") || role is "staff" || role.Contains("platform"))
                return true;
        }
        return false;
    }

    /// True management authority for the Issue/Incident feature: SchoolAdmin, SchoolOwner,
    /// Principal, or platform staff. Deliberately NOT the same as IsStaff — the staff-app
    /// auto-provisioning flow (Staff_EnsureLogin.sql) assigns every driver/conductor/sweeper/
    /// gardener/guard/peon the literal role "staff", which IsStaff treats as manager-equivalent.
    /// Using IsStaff here would let every ordinary reporter list/read/patch everyone else's
    /// issues, so this checks for an EXACT match against the manager-tier policy roles instead.
    public static bool IsIssueManager(ClaimsPrincipal user)
    {
        foreach (var claim in user.FindAll("role"))
        {
            var role = claim.Value.ToLowerInvariant();
            if (role == Policies.SchoolAdmin || role == Policies.SchoolOwner || role == Policies.Principal
                || role == Policies.PlatformOnly)
                return true;
        }
        return false;
    }

    /// Drivers, conductors, and staff may start/own live trips. Parents and students may not.
    public static bool CanOperateTrips(ClaimsPrincipal user)
    {
        if (IsStaff(user)) return true;
        foreach (var claim in user.FindAll("role"))
        {
            var role = claim.Value.ToLowerInvariant();
            if (role == "driver" || role.Contains("driver") || role == "conductor" || role.Contains("conductor"))
                return true;
        }
        return false;
    }
}
