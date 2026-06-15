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
        var g = app.MapGroup("/v1/auth").RequireRateLimiting("auth");

        g.MapPost("/login", async (LoginRequest req, AuthRepository users, IPasswordHasher hasher,
            IJwtTokenService jwt, IRefreshTokenStore tokens, ITenantContext tenant) =>
        {
            if (req.Email is null || req.Password is null)
                return Results.Json(ErrorEnvelope.From(new("invalid_credentials", "email and password required")),
                    statusCode: 422);

            // Credential lookup runs as a SYSTEM (platform) session: the caller's tenant is unknown
            // until the user is identified, and dbo.Users is RLS-protected — without this the row is
            // filtered out and every login fails. The issued token reflects the user's REAL
            // tenant/platform/roles (from the DB), not this lookup context.
            tenant.Set(null, null, isPlatform: true);
            var user = await users.GetByEmailAsync(req.Email);
            if (user?.PasswordHash is null || !hasher.Verify(req.Password, user.PasswordHash))
                return Results.Json(ErrorEnvelope.From(new("invalid_credentials", "bad email or password")),
                    statusCode: 401);

            var roles = await users.GetRolesAsync(user.Id); // from dbo.UserRoles, not hardcoded
            var access = jwt.IssueAccess(user.Id, user.TenantId, roles, user.IsPlatform);
            var refresh = jwt.NewRefreshToken();
            await tokens.SaveAsync(user.Id, Sha256(refresh), DateTime.UtcNow.AddDays(30));
            return Results.Ok(new DataEnvelope<TokenResponse>(new TokenResponse(access, refresh)));
        });

        g.MapPost("/refresh", async (RefreshRequest req, IRefreshTokenStore tokens, IJwtTokenService jwt,
            AuthRepository users, ITenantContext tenant) =>
        {
            var hash = Sha256(req.RefreshToken);
            var userId = await tokens.GetActiveUserIdAsync(hash);
            if (userId is null)
                return Results.Json(ErrorEnvelope.From(new("invalid_token", "refresh token invalid")),
                    statusCode: 401);
            await tokens.RevokeAsync(hash); // rotation
            var newRefresh = jwt.NewRefreshToken();
            await tokens.SaveAsync(userId.Value, Sha256(newRefresh), DateTime.UtcNow.AddDays(30));

            // Reload the user's REAL tenant/platform/roles before re-issuing the access token.
            // dbo.Users is RLS-protected, so the lookup runs as a system (platform) session, like login.
            tenant.Set(null, null, isPlatform: true);
            var user = await users.GetByIdAsync(userId.Value);
            if (user is null)
                return Results.Json(ErrorEnvelope.From(new("invalid_token", "user no longer exists")),
                    statusCode: 401);
            var roles = await users.GetRolesAsync(user.Id);
            var access = jwt.IssueAccess(user.Id, user.TenantId, roles, user.IsPlatform);
            return Results.Ok(new DataEnvelope<TokenResponse>(new TokenResponse(access, newRefresh)));
        });

        g.MapPost("/otp/request", async (OtpRequest req, AuthRepository users, IOtpSender otp,
            ITenantContext tenant) =>
        {
            // Lookups run as a system (platform) session — Users is RLS-protected and the caller is anon.
            tenant.Set(null, null, isPlatform: true);
            var isEmail = req.Identifier.Contains('@');
            var channel = isEmail ? "email" : "sms";
            var user = isEmail
                ? await users.GetByEmailAsync(req.Identifier)
                : await users.GetByPhoneAsync(req.Identifier);
            if (user is not null)
            {
                var code = await otp.SendAsync(req.Identifier, channel);
                await users.OtpInsertAsync(req.Identifier, channel, Sha256(code),
                    DateTime.UtcNow.AddMinutes(10));
            }
            // Always 200 — never leak whether the account exists.
            return Results.Ok(new DataEnvelope<object>(new { sent = true }));
        });

        g.MapPost("/otp/verify", async (OtpVerifyRequest req, AuthRepository users,
            IJwtTokenService jwt, IRefreshTokenStore tokens, ITenantContext tenant) =>
        {
            tenant.Set(null, null, isPlatform: true);
            var activeHash = await users.OtpActiveHashAsync(req.Identifier);
            if (activeHash is null || activeHash != Sha256(req.Code))
                return Results.Json(ErrorEnvelope.From(new("invalid_code", "code invalid or expired")),
                    statusCode: 401);
            await users.OtpConsumeAsync(req.Identifier, activeHash);

            var user = req.Identifier.Contains('@')
                ? await users.GetByEmailAsync(req.Identifier)
                : await users.GetByPhoneAsync(req.Identifier);
            if (user is null)
                return Results.Json(ErrorEnvelope.From(new("invalid_code", "user not found")),
                    statusCode: 401);

            var roles = await users.GetRolesAsync(user.Id);
            var access = jwt.IssueAccess(user.Id, user.TenantId, roles, user.IsPlatform);
            var refresh = jwt.NewRefreshToken();
            await tokens.SaveAsync(user.Id, Sha256(refresh), DateTime.UtcNow.AddDays(30));
            return Results.Ok(new DataEnvelope<TokenResponse>(new TokenResponse(access, refresh)));
        });

        g.MapPost("/set-password", async (SetPasswordRequest req, AuthRepository users,
            IPasswordHasher hasher, ITenantContext tenant) =>
        {
            if (tenant.UserId is not { } uid) return Results.Unauthorized();
            await users.SetPasswordAsync(uid, hasher.Hash(req.Password));
            return Results.NoContent();
        }).RequireAuthorization();

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
