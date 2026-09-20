namespace Sms.Application.Interfaces.DAO;

public interface IInvitationDao
{
    Task<Guid> CreateAsync(Guid tenantId, Guid userId, string? email, string? phone, string roleLabel,
        Guid? invitedByUserId, DateTime expiresAt, CancellationToken ct = default);
    Task<IReadOnlyList<InvitationRow>> ListByTenantAsync(Guid tenantId, CancellationToken ct = default);
    Task<InvitationRow?> GetByIdAsync(Guid tenantId, Guid id, CancellationToken ct = default);
    Task MarkResentAsync(Guid id, DateTime expiresAt, CancellationToken ct = default);
    Task MarkAcceptedByUserIdAsync(Guid userId, CancellationToken ct = default);
    Task MarkRevokedAsync(Guid id, CancellationToken ct = default);
}

public sealed record InvitationRow(
    Guid Id,
    Guid UserId,
    string? Email,
    string? Phone,
    string RoleLabel,
    DateTime InvitedAt,
    DateTime ExpiresAt,
    DateTime? AcceptedAt,
    DateTime? RevokedAt,
    DateTime? LastResentAt);
