using Sms.Application.Common;
using Sms.Modules.Transport;
using Sms.Shared.Kernel.Authz;
using Sms.Shared.Kernel.Tenancy;
using Sms.Shared.Kernel.Time;

namespace Sms.Application.Services.Transport;

/// Builds the live fleet board snapshot (shared by HTTP and SignalR).
public sealed class FleetSnapshotBuilder(BusRepository repo, ITenantContext tenant, ITenantFeatureSet features, IClock clock)
{
    public async Task<IReadOnlyList<FleetBusResponse>> BuildAsync(CancellationToken ct = default)
    {
        var rows = await repo.FleetAsync(ct);
        var teachers = (await repo.ListBusesAsync(ct)).ToDictionary(b => b.BusId);
        var now = clock.UtcNow;
        var gpsAllowed = FeatureGate.Allowed(tenant, features, FeatureCatalog.TransportGps);
        var list = new List<FleetBusResponse>(rows.Count);

        foreach (var r in rows)
        {
            teachers.TryGetValue(r.BusId, out var teacherRow);
            string status;
            string? nextStop = null;
            double? lat = r.Lat;
            double? lng = r.Lng;
            double? speed = r.SpeedKmh;
            DateTime? lastPing = r.LastPingAt;

            if (r.TripId is null || r.LastPingAt is null)
                status = "idle";
            else if (!gpsAllowed)
            {
                status = "idle";
                lat = null;
                lng = null;
                speed = null;
                lastPing = null;
            }
            else
            {
                var ageMin = (now - r.LastPingAt.Value).TotalMinutes;
                status = ageMin > 5 ? "delayed"
                    : (r.SpeedKmh is <= 3) ? "at_stop"
                    : "on_route";
                nextStop = (await repo.GetPositionAsync(r.BusId, ct)).NextStopName;
            }

            list.Add(new FleetBusResponse(
                r.BusId, r.RouteId, r.BusNo, r.RouteName, r.Driver, r.DriverPhone,
                r.StopCount, r.StudentsRiding, status,
                lat, lng, speed, nextStop, lastPing,
                teacherRow?.TeacherUserId, teacherRow?.TeacherName));
        }

        return list;
    }
}
