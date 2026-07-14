using Sms.Shared.Kernel.Auth;

namespace Sms.Application.Interfaces.DAO;

public interface IUserProvisioningDao
{
    Task<Guid> CreateUserAsync(Guid tenantId, string? email, string? phone, bool isPlatform, string[] roles, CancellationToken ct = default);
    Task<ImportResult> BulkCreateAsync(Guid tenantId, IReadOnlyList<ImportRow> rows, CancellationToken ct = default);
}
