using System.Security.Cryptography;
using System.Text;
using Sms.Api.Auth;
using Sms.Shared.Kernel.Auth;
using Sms.Shared.Kernel.Http;
using Sms.Shared.Kernel.Tenancy;

namespace Sms.Api.Endpoints;

public static class AuthEndpoints
{
    public static void MapAuth(this WebApplication app)
    {
        var g = app.MapGroup("/v1/auth");

        g.MapPost("/login", async (LoginRequest req, AuthRepository users, IPasswordHasher hasher,
            IJwtTokenService jwt, IRefreshTokenStore tokens, ITenantContext tenant) =>
        {
            if (req.Email is null || req.Password is null)
                return Results.Json(ErrorEnvelope.From(new("invalid_credentials", "email and password required")),
                    statusCode: 422);

            // Credential lookup runs as a SYSTEM (platform) session: the caller's tenant is unknown
            // until the user is identified, and dbo.Users is RLS-protected — without this the row is
            // filtered out and every login fails. The issued token reflects the user's REAL
            // tenant/platform (from the DB columns), not this lookup context.
            tenant.Set(null, null, isPlatform: true);
            var user = await users.GetByEmailAsync(req.Email);
            if (user?.PasswordHash is null || !hasher.Verify(req.Password, user.PasswordHash))
                return Results.Json(ErrorEnvelope.From(new("invalid_credentials", "bad email or password")),
                    statusCode: 401);

            var access = jwt.IssueAccess(user.Id, user.TenantId,
                roles: user.IsPlatform ? new[] { "platform.only" } : new[] { "school.admin" },
                isPlatform: user.IsPlatform);
            var refresh = jwt.NewRefreshToken();
            await tokens.SaveAsync(user.Id, Sha256(refresh), DateTime.UtcNow.AddDays(30));
            return Results.Ok(new DataEnvelope<TokenResponse>(new TokenResponse(access, refresh)));
        });

        g.MapPost("/refresh", async (RefreshRequest req, IRefreshTokenStore tokens, IJwtTokenService jwt) =>
        {
            var hash = Sha256(req.RefreshToken);
            var userId = await tokens.GetActiveUserIdAsync(hash);
            if (userId is null)
                return Results.Json(ErrorEnvelope.From(new("invalid_token", "refresh token invalid")),
                    statusCode: 401);
            await tokens.RevokeAsync(hash); // rotation
            var newRefresh = jwt.NewRefreshToken();
            await tokens.SaveAsync(userId.Value, Sha256(newRefresh), DateTime.UtcNow.AddDays(30));
            // Phase-0 minimal: access re-issued without reloading tenant/roles/platform from the user
            // record (hardened in Phase 1). RefreshTokens is not RLS-protected, so no system session needed.
            var access = jwt.IssueAccess(userId.Value, null, new[] { "school.admin" }, false);
            return Results.Ok(new DataEnvelope<TokenResponse>(new TokenResponse(access, newRefresh)));
        });

        g.MapGet("/me", (HttpContext http) =>
        {
            var sub = http.User.FindFirst("sub")?.Value;
            if (sub is null) return Results.Unauthorized();
            return Results.Ok(new DataEnvelope<object>(new
            {
                id = sub,
                tenant_id = http.User.FindFirst("tenant_id")?.Value,
                roles = http.User.FindAll("role").Select(c => c.Value).ToArray()
            }));
        }).RequireAuthorization();

        g.MapPost("/logout", async (RefreshRequest req, IRefreshTokenStore tokens) =>
        {
            await tokens.RevokeAsync(Sha256(req.RefreshToken));
            return Results.NoContent();
        });
    }

    private static string Sha256(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes);
    }
}
