using Sms.Application.Interfaces.DAO;
using Sms.Infrastructure.SQL;
using Sms.Shared.Kernel.Auth;
using Sms.Shared.Kernel.Data;

namespace Sms.Infrastructure.DAO;

public sealed class AuthDao(IDbConnectionFactory factory) : BaseRepository(factory), IAuthDao
{
    public async Task<UserRecord?> GetByEmailAsync(string email, CancellationToken ct = default) =>
        (await QueryProcAsync<UserRecord>(AuthQueries.GetByEmail, new { Email = email }, ct)).FirstOrDefault();

    public async Task<UserRecord?> GetByPhoneAsync(string phone, CancellationToken ct = default) =>
        (await QueryProcAsync<UserRecord>(AuthQueries.GetByPhone, new { Phone = phone }, ct)).FirstOrDefault();

    public Task<UserRecord?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        QuerySingleProcAsync<UserRecord>(AuthQueries.GetById, new { Id = id }, ct);

    public Task<IReadOnlyList<UserRecord>> ListByEmailAsync(string email, CancellationToken ct = default) =>
        QueryInlineAsync<UserRecord>(
            "SELECT Id, TenantId, Email, StudentId, Phone, PasswordHash, IsPlatform, Status " +
            "FROM dbo.Users WHERE Email = @Email " +
            "ORDER BY CASE WHEN IsPlatform = 1 THEN 0 ELSE 1 END, CreatedAt",
            new { Email = email }, ct);

    public Task<IReadOnlyList<UserRecord>> ListByPhoneAsync(string phone, CancellationToken ct = default) =>
        QueryInlineAsync<UserRecord>(
            "SELECT Id, TenantId, Email, StudentId, Phone, PasswordHash, IsPlatform, Status " +
            "FROM dbo.Users WHERE Phone = @Phone " +
            "ORDER BY CASE WHEN IsPlatform = 1 THEN 0 ELSE 1 END, CreatedAt",
            new { Phone = phone }, ct);

    public async Task<UserRecord?> GetByEmailAndTenantAsync(string email, Guid tenantId, CancellationToken ct = default) =>
        (await QueryInlineAsync<UserRecord>(
            "SELECT Id, TenantId, Email, StudentId, Phone, PasswordHash, IsPlatform, Status " +
            "FROM dbo.Users WHERE Email = @Email AND TenantId = @TenantId",
            new { Email = email, TenantId = tenantId }, ct)).FirstOrDefault();

    public Task<IReadOnlyList<string>> GetRolesAsync(Guid userId, CancellationToken ct = default) =>
        QueryProcAsync<string>(AuthQueries.GetRoles, new { UserId = userId }, ct);

    public Task SetPasswordAsync(Guid userId, string passwordHash, CancellationToken ct = default) =>
        ExecuteProcAsync(AuthQueries.SetPassword, new { UserId = userId, PasswordHash = passwordHash }, ct);

    public Task OtpInsertAsync(string identifier, string channel, string codeHash,
        DateTime expiresAt, CancellationToken ct = default) =>
        ExecuteProcAsync(AuthQueries.OtpInsert,
            new { Identifier = identifier, Channel = channel, CodeHash = codeHash, ExpiresAt = expiresAt }, ct);

    public async Task<string?> OtpActiveHashAsync(string identifier, CancellationToken ct = default)
    {
        var rows = await QueryProcAsync<OtpRow>(AuthQueries.OtpGetActive, new { Identifier = identifier }, ct);
        return rows.Count == 0 ? null : rows[0].CodeHash;
    }

    public Task OtpConsumeAsync(string identifier, string codeHash, CancellationToken ct = default) =>
        ExecuteProcAsync(AuthQueries.OtpConsume, new { Identifier = identifier, CodeHash = codeHash }, ct);

    public Task OtpConsumeAllAsync(string identifier, CancellationToken ct = default) =>
        ExecuteProcAsync(AuthQueries.OtpConsumeAll, new { Identifier = identifier }, ct);

    private sealed record OtpRow(Guid Id, string CodeHash);
}
