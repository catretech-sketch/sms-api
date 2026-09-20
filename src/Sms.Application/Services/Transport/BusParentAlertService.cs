using Microsoft.Extensions.Logging;
using Sms.Application.Services.Realtime;
using Sms.Modules.Comms;
using Sms.Modules.Transport;

namespace Sms.Application.Services.Transport;

public interface IBusParentAlertService
{
    Task NotifyTripStartedAsync(Guid tenantId, Guid busId, Guid tripId, CancellationToken ct = default);
    Task NotifyApproachingStopsAsync(
        Guid tenantId, Guid busId, Guid tripId, double lat, double lng, CancellationToken ct = default);
}

/// Fan-out in-app notices to each child's parent when a trip starts, and again when the bus
/// comes within 1 km of that child's assigned stop. Deduped per trip so GPS pings cannot spam.
public sealed class BusParentAlertService(
    StudentBusRepository riders,
    CommsRepository comms,
    ILiveBroadcaster live,
    ILogger<BusParentAlertService> logger) : IBusParentAlertService
{
    public Task NotifyTripStartedAsync(Guid tenantId, Guid busId, Guid tripId, CancellationToken ct = default) =>
        NotifyAsync(tenantId, busId, tripId, BusParentAlertRules.TripStarted, busLat: null, busLng: null, ct);

    public Task NotifyApproachingStopsAsync(
        Guid tenantId, Guid busId, Guid tripId, double lat, double lng, CancellationToken ct = default) =>
        NotifyAsync(tenantId, busId, tripId, BusParentAlertRules.ApproachingStop, lat, lng, ct);

    private async Task NotifyAsync(
        Guid tenantId, Guid busId, Guid tripId, string kind, double? busLat, double? busLng, CancellationToken ct)
    {
        try
        {
            var roster = await riders.ListRidersWithStopsAsync(busId, ct);
            var sent = false;
            foreach (var rider in roster)
            {
                if (kind == BusParentAlertRules.ApproachingStop)
                {
                    if (busLat is not { } lat || busLng is not { } lng
                        || rider.StopLat is not { } stopLat || rider.StopLng is not { } stopLng)
                        continue;
                    var meters = TripRepository.Haversine(lat, lng, stopLat, stopLng);
                    if (!BusParentAlertRules.IsWithinApproach(meters)) continue;
                }

                var parents = await riders.ListParentUserIdsAsync(rider.StudentId, rider.AdmissionNo, ct);
                foreach (var parentId in parents)
                {
                    if (!await riders.TryInsertParentAlertAsync(tenantId, tripId, rider.StudentId, parentId, kind, ct))
                        continue;
                    var (title, body) = Copy(kind, rider);
                    await comms.CreateNotificationAsync(tenantId,
                        new CreateNotificationRequest("bus", "bus", title, body, parentId), ct);
                    sent = true;
                }
            }

            if (sent)
                await live.PublishAsync(tenantId, LiveEventTypes.Notification, ct: ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Parent bus alert {Kind} failed for trip {Trip}", kind, tripId);
        }
    }

    private static (string Title, string Body) Copy(string kind, BusRiderStopRow rider)
    {
        var bus = string.IsNullOrWhiteSpace(rider.BusNo) ? "the bus" : $"Bus #{rider.BusNo}";
        var child = string.IsNullOrWhiteSpace(rider.StudentName) ? "your child" : rider.StudentName;
        if (kind == BusParentAlertRules.ApproachingStop)
        {
            var stop = string.IsNullOrWhiteSpace(rider.StopName) ? "the stop" : rider.StopName;
            return ("Bus near stop", $"{bus} is about 1 km from {child}'s stop ({stop}).");
        }
        return ("Bus started", $"{bus} has started today's trip for {child}.");
    }
}
