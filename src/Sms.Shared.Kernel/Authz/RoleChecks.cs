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

    /// Drivers (and staff) may start/own live trips. Parents and students may not.
    public static bool CanOperateTrips(ClaimsPrincipal user)
    {
        if (IsStaff(user)) return true;
        foreach (var claim in user.FindAll("role"))
        {
            var role = claim.Value.ToLowerInvariant();
            if (role == "driver" || role.Contains("driver"))
                return true;
        }
        return false;
    }
}
