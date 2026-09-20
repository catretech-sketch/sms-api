namespace Sms.Application.Services.Transport;

/// Single source of truth for "can this authenticated caller see this bus's
/// live position." Every check resolves server-side from the caller's own
/// identity (userId/tenantId/roles from their JWT) and existing DB
/// relationships — never from a client-supplied busId being trusted as
/// proof of access.
public interface ITransportAuthorizationResolver
{
    Task<bool> CanViewBusAsync(Guid callerUserId, Guid callerTenantId, IReadOnlyCollection<string> callerRoles, Guid busId, CancellationToken ct = default);

    /// Route-level equivalent of CanViewBusAsync, used by the road-following route
    /// geometry endpoint. A route is visible to a caller if any bus currently assigned
    /// to that route would itself be visible to them under CanViewBusAsync.
    Task<bool> CanViewRouteAsync(Guid callerUserId, Guid callerTenantId, IReadOnlyCollection<string> callerRoles, Guid routeId, CancellationToken ct = default);
}
