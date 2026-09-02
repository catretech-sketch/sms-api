using Sms.Modules.Staffing.Contracts;
using Sms.Shared.Kernel.Data;

namespace Sms.Modules.Staffing.Profile;

public sealed class ProfileRepository(IDbConnectionFactory factory) : BaseRepository(factory)
{
    public Task<IReadOnlyList<StaffDocumentResponse>> ListForUserAsync(
        Guid tenantId, Guid userId, CancellationToken ct = default) =>
        QueryProcAsync<StaffDocumentResponse>(
            "dbo.StaffDocuments_ListForUser", new { TenantId = tenantId, UserId = userId }, ct);
}
