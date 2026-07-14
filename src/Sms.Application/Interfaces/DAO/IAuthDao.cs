using Sms.Shared.Kernel.Auth;

namespace Sms.Application.Interfaces.DAO;

public interface IAuthDao
{
    Task<UserRecord?> GetByEmailAsync(string email, CancellationToken ct = default);
    Task<UserRecord?> GetByPhoneAsync(string phone, CancellationToken ct = default);
    Task<UserRecord?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<UserRecord>> ListByEmailAsync(string email, CancellationToken ct = default);
    Task<IReadOnlyList<UserRecord>> ListByPhoneAsync(string phone, CancellationToken ct = default);
    Task<UserRecord?> GetByEmailAndTenantAsync(string email, Guid tenantId, CancellationToken ct = default);
    Task<IReadOnlyList<string>> GetRolesAsync(Guid userId, CancellationToken ct = default);
    Task SetPasswordAsync(Guid userId, string passwordHash, CancellationToken ct = default);
    Task OtpInsertAsync(string identifier, string channel, string codeHash, DateTime expiresAt, CancellationToken ct = default);
    Task<string?> OtpActiveHashAsync(string identifier, CancellationToken ct = default);
    Task OtpConsumeAsync(string identifier, string codeHash, CancellationToken ct = default);
}
