namespace Sms.Shared.Kernel.Routing;

public sealed record RouteWaypoint(double Lat, double Lng);

public sealed record ComputedRouteGeometry(string EncodedPolyline, int DistanceMeters, int DurationSeconds);

/// Server-side-only Google Routes integration. The API key never leaves this class —
/// no controller, DTO, or frontend ever sees it.
public interface IGoogleRoutesClient
{
    /// Returns null on any failure (timeout, non-2xx, malformed body, provider not
    /// configured). Callers must treat null as "try cached geometry, else unavailable" —
    /// never throw this up to an HTTP 500.
    Task<ComputedRouteGeometry?> ComputeRouteAsync(
        IReadOnlyList<RouteWaypoint> orderedWaypoints, CancellationToken ct = default);
}
