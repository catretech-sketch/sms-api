using Sms.Application.Services.Transport;
using Sms.Modules.Transport;
using Sms.Shared.Kernel.Routing;
using Xunit;

namespace Sms.Tests.Unit.Transport;

public class RouteGeometryServiceTests
{
    static RouteStopListItem Stop(int seq, double lat, double lng) => new(Guid.NewGuid(), Guid.NewGuid(), "Stop", seq, lat, lng);

    [Fact]
    public async Task Returns_unavailable_without_calling_provider_when_fewer_than_two_stops()
    {
        var routeId = Guid.NewGuid();
        var stops = new FakeStopSource(new[] { Stop(1, 12.0, 77.0) });
        var client = new FakeGoogleRoutesClient(callsShouldFail: true); // would throw if called
        var repo = new FakeRouteGeometryStore();
        var service = new RouteGeometryService(stops, client, repo);

        var result = await service.GetAsync(Guid.NewGuid(), routeId);

        Assert.Equal(RouteGeometryStatus.Unavailable, result.Status);
        Assert.Null(result.Geometry);
        Assert.Equal(0, client.CallCount);
    }

    [Fact]
    public async Task Calls_provider_and_persists_on_hash_miss()
    {
        var routeId = Guid.NewGuid();
        var stops = new FakeStopSource(new[] { Stop(1, 12.0, 77.0), Stop(2, 12.1, 77.1) });
        var client = new FakeGoogleRoutesClient(result: new ComputedRouteGeometry("enc", 100, 10));
        var repo = new FakeRouteGeometryStore();
        var service = new RouteGeometryService(stops, client, repo);

        var result = await service.GetAsync(Guid.NewGuid(), routeId);

        Assert.Equal(RouteGeometryStatus.Available, result.Status);
        Assert.Equal("enc", result.Geometry);
        Assert.Equal(1, client.CallCount);
        Assert.NotNull(repo.Saved);
    }

    [Fact]
    public async Task Reuses_cached_geometry_on_hash_match_without_calling_provider()
    {
        var routeId = Guid.NewGuid();
        var orderedStops = new[] { Stop(1, 12.0, 77.0), Stop(2, 12.1, 77.1) };
        var stops = new FakeStopSource(orderedStops);
        var matchingHash = RouteGeometryHasher.Compute(orderedStops);
        var client = new FakeGoogleRoutesClient(callsShouldFail: true);
        var repo = new FakeRouteGeometryStore(existing: new RouteGeometryRow(
            routeId, matchingHash, "google-encoded-polyline", "cached", 50, 5, "google-routes", DateTime.UtcNow));
        var service = new RouteGeometryService(stops, client, repo);

        var result = await service.GetAsync(Guid.NewGuid(), routeId);

        Assert.Equal(RouteGeometryStatus.Available, result.Status);
        Assert.Equal("cached", result.Geometry);
        Assert.Equal(0, client.CallCount);
    }

    [Fact]
    public async Task Falls_back_to_stale_cache_when_provider_fails_on_hash_miss()
    {
        var routeId = Guid.NewGuid();
        var stops = new FakeStopSource(new[] { Stop(1, 12.0, 77.0), Stop(2, 12.1, 77.1) });
        var client = new FakeGoogleRoutesClient(result: null); // simulates provider failure
        var repo = new FakeRouteGeometryStore(existing: new RouteGeometryRow(
            routeId, "stale-hash", "google-encoded-polyline", "stale-geom", 1, 1, "google-routes", DateTime.UtcNow.AddDays(-1)));
        var service = new RouteGeometryService(stops, client, repo);

        var result = await service.GetAsync(Guid.NewGuid(), routeId);

        Assert.Equal(RouteGeometryStatus.Available, result.Status);
        Assert.Equal("stale-geom", result.Geometry);
    }

    [Fact]
    public async Task Returns_unavailable_when_provider_fails_and_no_cache_exists()
    {
        var routeId = Guid.NewGuid();
        var stops = new FakeStopSource(new[] { Stop(1, 12.0, 77.0), Stop(2, 12.1, 77.1) });
        var client = new FakeGoogleRoutesClient(result: null);
        var repo = new FakeRouteGeometryStore(existing: null);
        var service = new RouteGeometryService(stops, client, repo);

        var result = await service.GetAsync(Guid.NewGuid(), routeId);

        Assert.Equal(RouteGeometryStatus.Unavailable, result.Status);
        Assert.Null(result.Geometry);
    }
}

// --- fakes -----------------------------------------------------------------

sealed class FakeStopSource(IReadOnlyList<RouteStopListItem> stops) : IRouteStopSource
{
    public Task<IReadOnlyList<RouteStopListItem>> ListRouteStopsAsync(Guid routeId, CancellationToken ct = default) =>
        Task.FromResult(stops);
}

sealed class FakeGoogleRoutesClient : IGoogleRoutesClient
{
    readonly ComputedRouteGeometry? _result;
    readonly bool _callsShouldFail;
    public int CallCount { get; private set; }

    public FakeGoogleRoutesClient(ComputedRouteGeometry? result = null, bool callsShouldFail = false)
    {
        _result = result;
        _callsShouldFail = callsShouldFail;
    }

    public Task<ComputedRouteGeometry?> ComputeRouteAsync(IReadOnlyList<RouteWaypoint> orderedWaypoints, CancellationToken ct = default)
    {
        CallCount++;
        if (_callsShouldFail) throw new InvalidOperationException("provider should not have been called");
        return Task.FromResult(_result);
    }
}

sealed class FakeRouteGeometryStore : IRouteGeometryStore
{
    RouteGeometryRow? _existing;
    public RouteGeometryRow? Saved { get; private set; }

    public FakeRouteGeometryStore(RouteGeometryRow? existing = null) => _existing = existing;

    public Task<RouteGeometryRow?> GetAsync(Guid routeId, CancellationToken ct = default) => Task.FromResult(_existing);

    public Task UpsertAsync(Guid tenantId, Guid routeId, string stopSequenceHash, string format, string encodedPolyline,
        int distanceMeters, int durationSeconds, string provider, DateTime generatedAt, CancellationToken ct = default)
    {
        _existing = Saved = new RouteGeometryRow(routeId, stopSequenceHash, format, encodedPolyline, distanceMeters, durationSeconds, provider, generatedAt);
        return Task.CompletedTask;
    }
}
