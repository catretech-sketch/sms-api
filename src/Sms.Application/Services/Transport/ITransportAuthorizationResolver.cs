namespace Sms.Application.Services.Transport;

/// Single source of truth for "can this authenticated caller see this bus's
/// live position." Every check resolves server-side from the caller's own
/// identity (userId/tenantId/roles from their JWT) and existing DB
/// relationships — never from a client-supplied busId being trusted as
/// proof of access.
public interface ITransportAuthorizationResolver
{
    Task<bool> CanViewBusAsync(Guid callerUserId, Guid callerTenantId, IReadOnlyCollection<string> callerRoles, Guid busId, CancellationToken ct = default);
}
