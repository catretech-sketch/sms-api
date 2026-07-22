using Sms.Application.DTOs.Users;

namespace Sms.Application.Interfaces.DAO;

public interface IRoleTemplateDao
{
    Task<IReadOnlyList<RoleTemplateOverrideDto>> GetAsync(Guid tenantId, CancellationToken ct = default);
    Task SetAsync(Guid tenantId, IReadOnlyList<RoleTemplateOverrideDto> overrides, CancellationToken ct = default);
}
