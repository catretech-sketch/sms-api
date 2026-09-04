using Sms.Modules.Transport;

namespace Sms.Application.Services.Transport;

public interface ITransportFleetBroadcaster
{
    Task BroadcastFleetAsync(Guid tenantId, CancellationToken ct = default);
    Task BroadcastPositionAsync(Guid busId, BusLiveSnapshotResponse snapshot, CancellationToken ct = default);
    Task BroadcastTripStartedAsync(Guid busId, Guid tripId, Guid? driverId, Guid? conductorId, string direction, DateTime startedAt, CancellationToken ct = default);
    Task BroadcastTripEndedAsync(Guid busId, Guid tripId, DateTime endedAt, CancellationToken ct = default);
    Task BroadcastStatusChangedAsync(Guid busId, Guid tripId, string status, CancellationToken ct = default);
}
