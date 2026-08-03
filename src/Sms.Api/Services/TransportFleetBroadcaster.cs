using Microsoft.AspNetCore.SignalR;
using Sms.Api.Hubs;
using Sms.Application.Services.Transport;
using Sms.Shared.Kernel.Tenancy;

namespace Sms.Api.Services;

public sealed class TransportFleetBroadcaster(
    IHubContext<TransportFleetHub> hub,
    FleetSnapshotBuilder fleet,
    ITenantContext tenant) : ITransportFleetBroadcaster
{
    public async Task BroadcastFleetAsync(Guid tenantId, CancellationToken ct = default)
    {
        if (tenant.TenantId != tenantId) return;
        var snapshot = await fleet.BuildAsync(ct);
        await hub.Clients.Group(TransportFleetHub.TenantGroup(tenantId.ToString()))
            .SendAsync("fleet_update", snapshot, ct);
    }
}
