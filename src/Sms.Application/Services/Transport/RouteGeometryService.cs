using Sms.Modules.Transport;
using Sms.Shared.Kernel.Routing;

namespace Sms.Application.Services.Transport;

public enum RouteGeometryStatus { Available, Unavailable }

public sealed record RouteGeometryResult(
    Guid RouteId, RouteGeometryStatus Status, string? Format, string? Geometry,
    int? DistanceMeters, int? DurationSeconds, string StopSequenceHash, DateTime? GeneratedAt);

public interface IRouteGeometryService
{
    Task<RouteGeometryResult> GetAsync(Guid tenantId, Guid routeId, CancellationToken ct = default);
}

/// Orchestrates the planned-route geometry cache: reuse on hash match, regenerate via
/// Google Routes on a miss, and prefer stale cached geometry over "unavailable" whenever
/// the provider fails — this repo's product rule is "never show a fake straight line," not
/// "always show the freshest geometry."
public sealed class RouteGeometryService(
    IRouteStopSource stops, IGoogleRoutesClient routesClient, IRouteGeometryStore store) : IRouteGeometryService
{
    const string Format = "google-encoded-polyline";
    const string Provider = "google-routes";

    public async Task<RouteGeometryResult> GetAsync(Guid tenantId, Guid routeId, CancellationToken ct = default)
    {
        var orderedStops = await stops.ListRouteStopsAsync(routeId, ct);
        if (orderedStops.Count < 2)
            return Unavailable(routeId, RouteGeometryHasher.Compute(orderedStops));

        var hash = RouteGeometryHasher.Compute(orderedStops);
        var cached = await store.GetAsync(routeId, ct);
        if (cached is not null && cached.StopSequenceHash == hash)
            return Available(cached);

        var waypoints = orderedStops.Select(s => new RouteWaypoint(s.Lat, s.Lng)).ToList();
        var computed = await routesClient.ComputeRouteAsync(waypoints, ct);
        if (computed is null)
            return cached is not null ? Available(cached) : Unavailable(routeId, hash);

        var generatedAt = DateTime.UtcNow;
        await store.UpsertAsync(tenantId, routeId, hash, Format, computed.EncodedPolyline,
            computed.DistanceMeters, computed.DurationSeconds, Provider, generatedAt, ct);

        return new RouteGeometryResult(
            routeId, RouteGeometryStatus.Available, Format, computed.EncodedPolyline,
            computed.DistanceMeters, computed.DurationSeconds, hash, generatedAt);
    }

    static RouteGeometryResult Available(RouteGeometryRow row) => new(
        row.RouteId, RouteGeometryStatus.Available, row.Format, row.EncodedPolyline,
        row.DistanceMeters, row.DurationSeconds, row.StopSequenceHash, row.GeneratedAt);

    static RouteGeometryResult Unavailable(Guid routeId, string hash) => new(
        routeId, RouteGeometryStatus.Unavailable, null, null, null, null, hash, null);
}
