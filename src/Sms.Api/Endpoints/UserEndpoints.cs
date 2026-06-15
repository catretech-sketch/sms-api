using Sms.Shared.Kernel.Auth;
using Sms.Shared.Kernel.Authz;
using Sms.Shared.Kernel.Http;
using Sms.Shared.Kernel.Results;
using Sms.Shared.Kernel.Tenancy;

namespace Sms.Api.Endpoints;

public sealed record InviteUserRequest(string? Email, string? Phone, string[] Roles);
public sealed record ImportUsersRequest(ImportRowDto[] Rows);
public sealed record ImportRowDto(string? Email, string? Phone, string? Role);
public sealed record ImportError(int Row, string Reason);
public sealed record ImportResponse(int Created, int Skipped, IReadOnlyList<ImportError> Errors);

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

            // Validate each row; rejected rows are reported in errors[] (with their index), valid rows
            // go to the TVP bulk insert. created/skipped come from the proc (skipped = in-batch/existing dupes).
            var valid = new List<ImportRow>();
            var errors = new List<ImportError>();
            for (var i = 0; i < req.Rows.Length; i++)
            {
                var r = req.Rows[i];
                if (r.Email is null && r.Phone is null)
                    errors.Add(new ImportError(i, "email or phone required"));
                else if (r.Role is not null && !AssignableRoles.Contains(r.Role))
                    errors.Add(new ImportError(i, $"invalid role '{r.Role}'"));
                else
                    valid.Add(new ImportRow(r.Email, r.Phone, r.Role));
            }
            var result = await repo.BulkCreateAsync(tid, valid);
            return Results.Ok(new DataEnvelope<ImportResponse>(
                new ImportResponse(result.Created, result.Skipped, errors)));
        });
    }

    private static bool IsSchoolAdmin(HttpContext http) =>
        http.User.FindAll("role").Any(c => c.Value == Policies.SchoolAdmin);

    private static IResult Forbidden(string m) =>
        Results.Json(ErrorEnvelope.From(new Error("forbidden", m)), statusCode: 403);
    private static IResult Invalid(string m) =>
        Results.Json(ErrorEnvelope.From(new Error("invalid_request", m)), statusCode: 422);
}
