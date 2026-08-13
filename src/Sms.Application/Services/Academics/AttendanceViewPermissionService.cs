using System.Security.Claims;
using Sms.Application.Interfaces.DAO;
using Sms.Shared.Kernel.Tenancy;

namespace Sms.Application.Services.Academics;

public interface IAttendanceViewPermissionService
{
    Task<bool> CanViewAsync(ClaimsPrincipal caller, CancellationToken ct = default);
}

public sealed class AttendanceViewPermissionService(
    IUserProvisioningDao users,
    IRoleTemplateDao roleTemplates,
    ITenantContext tenant) : IAttendanceViewPermissionService
{
    private static readonly HashSet<string> DefaultViewRoles = new(
        ["admin", "owner", "principal", "vice_principal", "teacher", "staff"],
        StringComparer.OrdinalIgnoreCase);

    public async Task<bool> CanViewAsync(ClaimsPrincipal caller, CancellationToken ct = default)
    {
        if (tenant.TenantId is not { } tenantId || tenant.UserId is not { } userId)
            return false;

        var roles = caller.FindAll("role")
            .Select(c => NormalizeRole(c.Value))
            .Where(r => r.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var templates = await roleTemplates.GetAsync(tenantId, ct);
        var allowedByRole = roles.Any(role =>
        {
            var templateRole = role == "owner" ? "admin" : role;
            var allowed = DefaultViewRoles.Contains(role);
            var roleOverride = templates.FirstOrDefault(o =>
                string.Equals(o.Role, templateRole, StringComparison.OrdinalIgnoreCase)
                && string.Equals(o.Module, "attendance", StringComparison.OrdinalIgnoreCase)
                && string.Equals(o.Cap, "V", StringComparison.OrdinalIgnoreCase));
            return roleOverride?.Effect.ToLowerInvariant() switch
            {
                "grant" => true,
                "revoke" => false,
                _ => allowed,
            };
        });

        var userOverrides = await users.GetPermissionsAsync(userId, ct);
        var userOverride = userOverrides.FirstOrDefault(o =>
            string.Equals(o.Module, "attendance", StringComparison.OrdinalIgnoreCase)
            && string.Equals(o.Cap, "V", StringComparison.OrdinalIgnoreCase));
        return userOverride?.Effect.ToLowerInvariant() switch
        {
            "grant" => true,
            "revoke" => false,
            _ => allowedByRole,
        };
    }

    private static string NormalizeRole(string role) =>
        (role ?? "").Trim().ToLowerInvariant().Split('.').LastOrDefault()?.Replace('-', '_') ?? "";
}
