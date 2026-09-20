using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Sms.Application.Services.Transport;
using Sms.Shared.Kernel.Authz;

namespace Sms.Api.Hubs;

[Authorize]
public sealed class TransportFleetHub(ITransportAuthorizationResolver authz) : Hub
{
    public override async Task OnConnectedAsync()
    {
        var tenantId = Context.User?.FindFirst("tenant_id")?.Value;
        var roles = Context.User?.FindAll("role").Select(c => c.Value).ToArray() ?? [];
        // Only Principal-tier callers get the tenant-wide fleet feed automatically —
        // everyone else (teacher/parent/driver) must call JoinBus for their one
        // authorized bus.
        if (tenantId is not null && (roles.Contains(Policies.Principal) || roles.Contains(Policies.SchoolAdmin) || roles.Contains(Policies.SchoolOwner)))
            await Groups.AddToGroupAsync(Context.ConnectionId, TenantGroup(tenantId));
        await base.OnConnectedAsync();
    }

    /// Joins the caller to this bus's live-position group iff
    /// ITransportAuthorizationResolver says they're allowed to see it.
    /// Returns false (not a thrown exception) on denial, so a caller with
    /// multiple pending JoinBus calls doesn't lose its whole connection over
    /// one unauthorized bus.
    public async Task<bool> JoinBus(Guid busId)
    {
        var (userId, tenantId, roles) = CallerClaims();
        if (userId is null || tenantId is null) return false;
        if (!await authz.CanViewBusAsync(userId.Value, tenantId.Value, roles, busId))
            return false;
        await Groups.AddToGroupAsync(Context.ConnectionId, BusGroup(busId));
        return true;
    }

    public Task LeaveBus(Guid busId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, BusGroup(busId));

    private (Guid? UserId, Guid? TenantId, string[] Roles) CallerClaims()
    {
        var uid = Context.User?.FindFirst("sub")?.Value;
        var tid = Context.User?.FindFirst("tenant_id")?.Value;
        var roles = Context.User?.FindAll("role").Select(c => c.Value).ToArray() ?? [];
        return (Guid.TryParse(uid, out var u) ? u : null, Guid.TryParse(tid, out var t) ? t : null, roles);
    }

    public static string TenantGroup(string tenantId) => $"transport-fleet:{tenantId}";
    public static string BusGroup(Guid busId) => $"bus:{busId}";
}
