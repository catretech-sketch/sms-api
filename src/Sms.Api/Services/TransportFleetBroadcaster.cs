using Microsoft.AspNetCore.SignalR;
using Sms.Api.Hubs;
using Sms.Application.Services.Transport;
using Sms.Modules.Transport;
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

    public async Task BroadcastPositionAsync(Guid busId, BusLiveSnapshotResponse snapshot, CancellationToken ct = default) =>
        await hub.Clients.Group(TransportFleetHub.BusGroup(busId)).SendAsync("position_update", snapshot, ct);

    public async Task BroadcastTripStartedAsync(Guid busId, Guid tripId, Guid? driverId, Guid? conductorId, string direction, DateTime startedAt, CancellationToken ct = default) =>
        await hub.Clients.Group(TransportFleetHub.BusGroup(busId)).SendAsync("trip_started",
            new { busId, tripId, driverId, conductorId, direction, startedAt }, ct);

    public async Task BroadcastTripEndedAsync(Guid busId, Guid tripId, DateTime endedAt, CancellationToken ct = default) =>
        await hub.Clients.Group(TransportFleetHub.BusGroup(busId)).SendAsync("trip_ended",
            new { busId, tripId, endedAt }, ct);

    public async Task BroadcastStatusChangedAsync(Guid busId, Guid tripId, string status, CancellationToken ct = default) =>
        await hub.Clients.Group(TransportFleetHub.BusGroup(busId)).SendAsync("status_changed",
            new { busId, tripId, status }, ct);
}
