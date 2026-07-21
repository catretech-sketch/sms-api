using Sms.Application.Interfaces.DAO;
using Sms.Shared.Kernel.Data;

namespace Sms.Infrastructure.DAO;

public sealed class InvitationDao(IDbConnectionFactory factory) : BaseRepository(factory), IInvitationDao
{
    public Task<Guid> CreateAsync(Guid tenantId, Guid userId, string? email, string? phone, string roleLabel,
        Guid? invitedByUserId, DateTime expiresAt, CancellationToken ct = default) =>
        QuerySingleProcAsync<Guid>("dbo.Invitations_Create",
            new
            {
                TenantId = tenantId, UserId = userId, Email = email, Phone = phone,
                RoleLabel = roleLabel, InvitedByUserId = invitedByUserId, ExpiresAt = expiresAt,
            }, ct)!;

    public Task<IReadOnlyList<InvitationRow>> ListByTenantAsync(Guid tenantId, CancellationToken ct = default) =>
        QueryProcAsync<InvitationRow>("dbo.Invitations_ListByTenant", new { TenantId = tenantId }, ct);

    public Task<InvitationRow?> GetByIdAsync(Guid tenantId, Guid id, CancellationToken ct = default) =>
        QuerySingleProcAsync<InvitationRow>("dbo.Invitations_GetById", new { TenantId = tenantId, Id = id }, ct);

    public Task MarkResentAsync(Guid id, DateTime expiresAt, CancellationToken ct = default) =>
        ExecuteProcAsync("dbo.Invitations_MarkResent", new { Id = id, ExpiresAt = expiresAt }, ct);

    public Task MarkAcceptedByUserIdAsync(Guid userId, CancellationToken ct = default) =>
        ExecuteProcAsync("dbo.Invitations_MarkAcceptedByUserId", new { UserId = userId }, ct);

    public Task MarkRevokedAsync(Guid id, CancellationToken ct = default) =>
        ExecuteProcAsync("dbo.Invitations_MarkRevoked", new { Id = id }, ct);
}
