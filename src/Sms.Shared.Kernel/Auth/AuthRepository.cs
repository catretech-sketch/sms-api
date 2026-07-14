using Sms.Shared.Kernel.Data;

namespace Sms.Shared.Kernel.Auth;

public sealed class AuthRepository(IDbConnectionFactory factory) : BaseRepository(factory)
{
    public async Task<UserRecord?> GetByEmailAsync(string email, CancellationToken ct = default) =>
        (await QueryProcAsync<UserRecord>("dbo.User_GetByEmail", new { Email = email }, ct)).FirstOrDefault();

    public async Task<UserRecord?> GetByPhoneAsync(string phone, CancellationToken ct = default) =>
        (await QueryProcAsync<UserRecord>("dbo.User_GetByPhone", new { Phone = phone }, ct)).FirstOrDefault();

    public Task<UserRecord?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        QuerySingleProcAsync<UserRecord>("dbo.User_GetById", new { Id = id }, ct);

    public Task<IReadOnlyList<string>> GetRolesAsync(Guid userId, CancellationToken ct = default) =>
        QueryProcAsync<string>("dbo.UserRoles_GetByUser", new { UserId = userId }, ct);

    public Task SetPasswordAsync(Guid userId, string passwordHash, CancellationToken ct = default) =>
        ExecuteProcAsync("dbo.User_SetPassword", new { UserId = userId, PasswordHash = passwordHash }, ct);

    public Task OtpInsertAsync(string identifier, string channel, string codeHash,
        DateTime expiresAt, CancellationToken ct = default) =>
        ExecuteProcAsync("dbo.Otp_Insert",
            new { Identifier = identifier, Channel = channel, CodeHash = codeHash, ExpiresAt = expiresAt }, ct);

    /// Returns the active code's stored hash, or null when none is active.
    public async Task<string?> OtpActiveHashAsync(string identifier, CancellationToken ct = default)
    {
        var rows = await QueryProcAsync<OtpRow>("dbo.Otp_GetActive", new { Identifier = identifier }, ct);
        return rows.Count == 0 ? null : rows[0].CodeHash;
    }

    public Task OtpConsumeAsync(string identifier, string codeHash, CancellationToken ct = default) =>
        ExecuteProcAsync("dbo.Otp_Consume", new { Identifier = identifier, CodeHash = codeHash }, ct);

    private sealed record OtpRow(Guid Id, string CodeHash);
}
