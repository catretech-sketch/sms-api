using Sms.Shared.Kernel.Auth;
using Sms.Shared.Kernel.Authz;
using Sms.Shared.Kernel.Http;
using Sms.Shared.Kernel.Results;
using Sms.Shared.Kernel.Tenancy;

namespace Sms.Api.Endpoints;

public sealed record InviteUserRequest(string? Email, string? Phone, string[] Roles);
public sealed record ImportUsersRequest(ImportRowDto[] Rows);
public sealed record ImportRowDto(string? Email, string? Phone, string? Role);

public static class UserEndpoints
{
    private static readonly HashSet<string> AssignableRoles = new(
        Policies.All.Where(r => r != Policies.PlatformOnly), StringComparer.OrdinalIgnoreCase);

    public static void MapUsers(this WebApplication app)
    {
        var g = app.MapGroup("/v1").RequireAuthorization();

        g.MapPost("/users", async (InviteUserRequest req, UserProvisioningRepository repo,
            ITenantContext tenant, HttpContext http) =>
        {
            if (!IsSchoolAdmin(http)) return Forbidden("school admin only");
            if (tenant.TenantId is not { } tid) return Forbidden("no tenant context");
            if (req.Email is null && req.Phone is null)
                return Invalid("email or phone required");
            if (req.Roles.Length == 0 || req.Roles.Any(r => !AssignableRoles.Contains(r)))
                return Invalid("invalid role(s)");

            var id = await repo.CreateUserAsync(tid, req.Email, req.Phone, false, req.Roles);
            return Results.Json(new DataEnvelope<object>(new { id }), statusCode: 201);
        });

        g.MapPost("/users/import", async (ImportUsersRequest req, UserProvisioningRepository repo,
            ITenantContext tenant, HttpContext http) =>
        {
            if (!IsSchoolAdmin(http)) return Forbidden("school admin only");
            if (tenant.TenantId is not { } tid) return Forbidden("no tenant context");

            var rows = req.Rows
                .Where(r => (r.Email is not null || r.Phone is not null)
                            && (r.Role is null || AssignableRoles.Contains(r.Role)))
                .Select(r => new ImportRow(r.Email, r.Phone, r.Role))
                .ToList();
            var result = await repo.BulkCreateAsync(tid, rows);
            return Results.Ok(new DataEnvelope<ImportResult>(result));
        });
    }

    private static bool IsSchoolAdmin(HttpContext http) =>
        http.User.FindAll("role").Any(c => c.Value == Policies.SchoolAdmin);

    private static IResult Forbidden(string m) =>
        Results.Json(ErrorEnvelope.From(new Error("forbidden", m)), statusCode: 403);
    private static IResult Invalid(string m) =>
        Results.Json(ErrorEnvelope.From(new Error("invalid_request", m)), statusCode: 422);
}
