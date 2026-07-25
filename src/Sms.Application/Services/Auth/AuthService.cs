using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Sms.Application.DTOs.Auth;
using Sms.Application.Common;
using Sms.Application.Interfaces.DAO;
using Sms.Modules.Tenancy.Data;
using Sms.Shared.Kernel.Auth;
using Sms.Shared.Kernel.Results;
using Sms.Shared.Kernel.Tenancy;

namespace Sms.Application.Services.Auth;

public interface IAuthService
{
    Task<ApiResult<TokenResponse>> LoginAsync(LoginRequest req, CancellationToken ct = default);
    Task<ApiResult<TokenResponse>> RefreshAsync(RefreshRequest req, CancellationToken ct = default);
    Task<ApiResult<object>> RequestOtpAsync(OtpRequest req, CancellationToken ct = default);
    Task<ApiResult<TokenResponse>> VerifyOtpAsync(OtpVerifyRequest req, CancellationToken ct = default);
    Task<ApiResult<object>> ForgotPasswordAsync(ForgotPasswordRequest req, CancellationToken ct = default);
    /// <summary>Onboard invite: welcome email/SMS with school name + password-setup OTP
    /// (or a magic login link, when method is "link").</summary>
    Task<ApiResult<object>> SendInviteSetupAsync(
        string identifier, string schoolName, string? roleLabel = null, TimeSpan? validFor = null,
        string method = "code", string? customMessage = null, CancellationToken ct = default);
    Task<ApiResult> ResetPasswordAsync(ResetPasswordRequest req, CancellationToken ct = default);
    Task<ApiResult> SetPasswordAsync(SetPasswordRequest req, CancellationToken ct = default);
    Task<ApiResult<object>> GetMeAsync(ClaimsPrincipal user, CancellationToken ct = default);
    Task<ApiResult> LogoutAsync(RefreshRequest req, CancellationToken ct = default);
}

public sealed class AuthService(
    IAuthDao users,
    IPasswordHasher hasher,
    IJwtTokenService jwt,
    IRefreshTokenStore tokens,
    IOtpSender otp,
    IEmailQueue emailQueue,
    ISmsSender sms,
    FrontendOptions frontend,
    ClientRepository clients,
    ITenantContext tenant,
    IInvitationDao invitations,
    IProfileDao profiles) : IAuthService
{
    public async Task<ApiResult<TokenResponse>> LoginAsync(LoginRequest req, CancellationToken ct = default)
    {
        var identifier = !string.IsNullOrWhiteSpace(req.Email) ? req.Email : req.Phone;
        if (string.IsNullOrWhiteSpace(identifier) || req.Password is null)
            return ApiResult<TokenResponse>.Fail(new Error("invalid_credentials", "email or phone and password required"), 422);

        tenant.Set(null, null, isPlatform: true);
        var user = await FindUserByPasswordAsync(identifier, req.Password, ct);
        if (user is null)
            return ApiResult<TokenResponse>.Fail(new Error("invalid_credentials", "bad email or password"), 401);
        if (AccessBlockedError(user) is { } blocked)
            return ApiResult<TokenResponse>.Fail(blocked, 403);

        return await IssueTokensAsync(user, ct);
    }

    /// Null when the row is free to sign in; an Error when it's "removed" or "inactive"
    /// (paused/removed by the school admin) — same wording either path (password/OTP).
    private static Error? AccessBlockedError(UserRecord user) => user.Status switch
    {
        "removed" => new Error("access_removed", "Your access to this school has been removed by the admin."),
        "inactive" => new Error("access_inactive", "Your access to this school has been deactivated by the admin."),
        _ => null,
    };

    public async Task<ApiResult<TokenResponse>> RefreshAsync(RefreshRequest req, CancellationToken ct = default)
    {
        var hash = Sha256(req.RefreshToken);
        var userId = await tokens.GetActiveUserIdAsync(hash, ct);
        if (userId is null)
            return ApiResult<TokenResponse>.Fail(new Error("invalid_token", "refresh token invalid"), 401);

        await tokens.RevokeAsync(hash, ct);
        var newRefresh = jwt.NewRefreshToken();
        await tokens.SaveAsync(userId.Value, Sha256(newRefresh), DateTime.UtcNow.AddDays(30), ct);

        tenant.Set(null, null, isPlatform: true);
        var user = await users.GetByIdAsync(userId.Value, ct);
        if (user is null)
            return ApiResult<TokenResponse>.Fail(new Error("invalid_token", "user no longer exists"), 401);

        var roles = await users.GetRolesAsync(user.Id, ct);
        var access = jwt.IssueAccess(user.Id, user.TenantId, roles, user.IsPlatform);
        return ApiResult<TokenResponse>.Ok(new TokenResponse(access, newRefresh));
    }

    public Task<ApiResult<object>> RequestOtpAsync(OtpRequest req, CancellationToken ct = default) =>
        SendOtpToRegisteredAsync(req.Identifier, ct);

    public async Task<ApiResult<TokenResponse>> VerifyOtpAsync(OtpVerifyRequest req, CancellationToken ct = default)
    {
        tenant.Set(null, null, isPlatform: true);
        var activeHash = await users.OtpActiveHashAsync(req.Identifier, ct);
        if (activeHash is null || activeHash != Sha256(req.Code))
            return ApiResult<TokenResponse>.Fail(new Error("invalid_code", "code invalid or expired"), 401);

        await users.OtpConsumeAsync(req.Identifier, activeHash, ct);
        var user = await FindUserByIdentifierAsync(req.Identifier, ct);
        if (user is null)
            return ApiResult<TokenResponse>.Fail(new Error("invalid_code", "user not found"), 401);
        if (AccessBlockedError(user) is { } blocked)
            return ApiResult<TokenResponse>.Fail(blocked, 403);

        return await IssueTokensAsync(user, ct);
    }

    public async Task<ApiResult<object>> ForgotPasswordAsync(ForgotPasswordRequest req, CancellationToken ct = default)
    {
        /* First-time / invite users (no password yet): welcome email with school name + OTP.
           Existing users: keep the generic verification-code mail. */
        tenant.Set(null, null, isPlatform: true);
        var user = await FindUserByIdentifierAsync(req.Identifier, ct);
        if (user is not null && string.IsNullOrEmpty(user.PasswordHash))
        {
            var schoolName = "your school";
            string? roleLabel = null;
            if (user.TenantId is Guid tid)
            {
                var school = await clients.GetAsync(tid, ct);
                if (!string.IsNullOrWhiteSpace(school?.Name)) schoolName = school.Name;
                var roles = await users.GetRolesAsync(user.Id, ct);
                roleLabel = RoleLabel(roles.FirstOrDefault());
            }
            return await SendInviteSetupAsync(req.Identifier, schoolName, roleLabel, TimeSpan.FromHours(24), ct: ct);
        }
        return await SendOtpToRegisteredAsync(req.Identifier, ct);
    }

    public async Task<ApiResult<object>> SendInviteSetupAsync(
        string identifier, string schoolName, string? roleLabel = null, TimeSpan? validFor = null,
        string method = "code", string? customMessage = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(identifier))
            return ApiResult<object>.Fail(new Error("invalid_request", "email or phone required"), 422);

        tenant.Set(null, null, isPlatform: true);
        var id = identifier.Trim();
        var isEmail = id.Contains('@');
        var channel = isEmail ? "email" : "sms";
        var user = await FindUserByIdentifierAsync(id, ct);
        if (user is null)
            return ApiResult<object>.Fail(new Error("not_registered",
                isEmail ? "Email is not registered." : "Phone is not registered."), 404);

        // "link" method: a long opaque token flows through the exact same OTP
        // storage/consume path as a 6-digit code (Otp_Insert/Otp_Consume compare a
        // SHA256 hash either way) — only the value shape and delivery text differ.
        var useLink = string.Equals(method, "link", StringComparison.OrdinalIgnoreCase);
        var code = useLink
            ? Convert.ToHexString(RandomNumberGenerator.GetBytes(24))
            : RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        var expiresAt = DateTime.UtcNow.Add(validFor ?? TimeSpan.FromMinutes(10));
        await users.OtpInsertAsync(id, channel, Sha256(code), expiresAt, ct);

        var link = useLink
            ? $"{frontend.BaseUrl.TrimEnd('/')}/?identifier={Uri.EscapeDataString(id)}&code={Uri.EscapeDataString(code)}"
            : null;

        if (isEmail)
            emailQueue.Enqueue(InviteWelcomeEmail.Build(id, schoolName, code, roleLabel, link, customMessage));
        else
            await sms.SendAsync(id, InviteWelcomeEmail.SmsBody(schoolName, code, roleLabel, link, customMessage), ct);

        return ApiResult<object>.Ok(new { sent = true });
    }

    private static string? RoleLabel(string? role) => role?.ToLowerInvariant() switch
    {
        "school.owner" => "Owner",
        "school.admin" => "Admin",
        "school.principal" => "Principal",
        "school.vice_principal" or "school.vice-principal" => "Vice-Principal",
        "school.teacher" => "Teacher",
        "staff" => "Staff",
        _ => role,
    };

    public async Task<ApiResult> ResetPasswordAsync(ResetPasswordRequest req, CancellationToken ct = default)
    {
        if (req.Password is null || req.Password.Length < 8)
            return ApiResult.Fail(new Error("weak_password", "password must be at least 8 characters"), 422);

        tenant.Set(null, null, isPlatform: true);
        var activeHash = await users.OtpActiveHashAsync(req.Identifier, ct);
        if (activeHash is null || req.Code is null || activeHash != Sha256(req.Code))
            return ApiResult.Fail(new Error("invalid_code", "code invalid or expired"), 401);

        await users.OtpConsumeAsync(req.Identifier, activeHash, ct);
        // Same identifier can own several tenant-scoped rows (invited to multiple
        // schools) — set the password on ALL of them so one setup step works
        // everywhere they were invited, not just the row a tiebreak happens to pick.
        var peers = await ListByIdentifierAsync(req.Identifier, ct);
        if (peers.Count == 0)
            return ApiResult.Fail(new Error("invalid_code", "user not found"), 401);

        var passwordHash = hasher.Hash(req.Password);
        foreach (var peer in peers)
        {
            await users.SetPasswordAsync(peer.Id, passwordHash, ct);
            await invitations.MarkAcceptedByUserIdAsync(peer.Id, ct);
        }
        return ApiResult.NoContent();
    }

    public async Task<ApiResult> SetPasswordAsync(SetPasswordRequest req, CancellationToken ct = default)
    {
        if (tenant.UserId is not { } uid)
            return ApiResult.Fail(new Error("unauthorized", "unauthorized"), 401);
        await users.SetPasswordAsync(uid, hasher.Hash(req.Password), ct);
        return ApiResult.NoContent();
    }

    public async Task<ApiResult<object>> GetMeAsync(ClaimsPrincipal user, CancellationToken ct = default)
    {
        var sub = user.FindFirst("sub")?.Value;
        if (sub is null || !Guid.TryParse(sub, out var userId))
            return ApiResult<object>.Fail(new Error("unauthorized", "unauthorized"), 401);

        var record = await users.GetByIdAsync(userId, ct);
        if (record is null)
            return ApiResult<object>.Fail(new Error("unauthorized", "unauthorized"), 401);

        var roles = user.FindAll("role").Select(c => c.Value).ToArray();
        var (title, classroom) = await ResolveProfileAsync(record.Id, roles, ct);

        string? tenantName = null;
        if (record.TenantId is Guid tid)
        {
            var school = await clients.GetAsync(tid, ct);
            tenantName = school?.Name;
        }

        return ApiResult<object>.Ok(new
        {
            id = sub,
            tenant_id = user.FindFirst("tenant_id")?.Value,
            roles,
            is_platform = user.FindFirst("is_platform")?.Value == "1",
            name = record.Name,
            email = record.Email,
            phone = record.Phone,
            tenant_name = tenantName,
            must_set_password = record.MustSetPassword,
            title,
            classroom,
            photo_url = record.PhotoUrl,
        });
    }

    /// Role-agnostic profile resolution: base identity always comes from Users (already
    /// on `record`); role-specific fields are looked up via a small dispatch so adding a
    /// Parent/Student branch later (for sms-staff/sms-student) is additive, not a rewrite.
    private async Task<(string? Title, string? Classroom)> ResolveProfileAsync(
        Guid userId, IReadOnlyList<string> roles, CancellationToken ct)
    {
        var lastSegment = roles.Select(r => r.Split('.').LastOrDefault() ?? r).FirstOrDefault();
        return lastSegment switch
        {
            "teacher" => (
                await profiles.GetTeacherTitleByUserIdAsync(userId, ct),
                await profiles.GetClassroomNameByTeacherUserIdAsync(userId, ct)),
            "principal" => (null, null),
            _ => (await profiles.GetStaffTitleByUserIdAsync(userId, ct), null),
        };
    }

    public async Task<ApiResult> LogoutAsync(RefreshRequest req, CancellationToken ct = default)
    {
        await tokens.RevokeAsync(Sha256(req.RefreshToken), ct);
        return ApiResult.NoContent();
    }

    private async Task<ApiResult<TokenResponse>> IssueTokensAsync(UserRecord user, CancellationToken ct)
    {
        var roles = await users.GetRolesAsync(user.Id, ct);
        var access = jwt.IssueAccess(user.Id, user.TenantId, roles, user.IsPlatform);
        var refresh = jwt.NewRefreshToken();
        await tokens.SaveAsync(user.Id, Sha256(refresh), DateTime.UtcNow.AddDays(30), ct);
        return ApiResult<TokenResponse>.Ok(new TokenResponse(access, refresh));
    }

    private async Task<ApiResult<object>> SendOtpToRegisteredAsync(string identifier, CancellationToken ct)
    {
        tenant.Set(null, null, isPlatform: true);
        var isEmail = identifier.Contains('@');
        var channel = isEmail ? "email" : "sms";
        var user = await FindUserByIdentifierAsync(identifier, ct);

        if (user is null)
            return ApiResult<object>.Fail(new Error("not_registered",
                isEmail ? "Email is not registered." : "Phone is not registered."), 404);

        var code = await otp.SendAsync(identifier, channel);
        await users.OtpInsertAsync(identifier, channel, Sha256(code), DateTime.UtcNow.AddMinutes(10), ct);
        return ApiResult<object>.Ok(new { sent = true });
    }

    /// <summary>
    /// Resolves login across multi-tenant peers that share the same email/phone.
    /// Picks the first password match, preferring platform accounts, and preferring
    /// a fully-active row over a removed/inactive one when several match (so losing
    /// access to one school never blocks signing in to another with the same creds).
    /// </summary>
    private async Task<UserRecord?> FindUserByPasswordAsync(string identifier, string password, CancellationToken ct)
    {
        var candidates = await ListByIdentifierAsync(identifier, ct);
        return candidates
            .Where(u => u.PasswordHash is not null && hasher.Verify(password, u.PasswordHash))
            .OrderByDescending(u => u.IsPlatform)
            .ThenBy(u => AccessBlockedError(u) is null ? 0 : 1)
            .FirstOrDefault();
    }

    private async Task<UserRecord?> FindUserByIdentifierAsync(string identifier, CancellationToken ct)
    {
        var candidates = await ListByIdentifierAsync(identifier, ct);
        return candidates
            .OrderByDescending(u => u.IsPlatform)
            .ThenBy(u => AccessBlockedError(u) is null ? 0 : 1)
            .FirstOrDefault();
    }

    private Task<IReadOnlyList<UserRecord>> ListByIdentifierAsync(string identifier, CancellationToken ct) =>
        identifier.Contains('@')
            ? users.ListByEmailAsync(identifier, ct)
            : users.ListByPhoneAsync(identifier, ct);

    private static string Sha256(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes);
    }
}
