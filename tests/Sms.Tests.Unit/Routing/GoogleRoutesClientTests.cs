using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sms.Shared.Kernel.Routing;
using Xunit;

namespace Sms.Tests.Unit.Routing;

public class GoogleRoutesClientTests
{
    static IReadOnlyList<RouteWaypoint> Waypoints(int count) =>
        Enumerable.Range(0, count).Select(i => new RouteWaypoint(12.0 + i * 0.001, 77.0 + i * 0.001)).ToList();

    [Fact]
    public async Task Returns_null_when_not_configured()
    {
        var options = Options.Create(new GoogleRoutesOptions { ApiKey = "" });
        var handler = new StubHttpMessageHandler((_, _) =>
            throw new InvalidOperationException("should not call Google Routes when not configured"));
        var factory = new SingleClientHttpClientFactory("google-routes", new HttpClient(handler));
        var client = new GoogleRoutesClient(factory, options, NullLogger<GoogleRoutesClient>.Instance);

        var result = await client.ComputeRouteAsync(Waypoints(2));

        Assert.Null(result);
    }

    [Fact]
    public async Task Returns_null_on_non_success_response()
    {
        var options = Options.Create(new GoogleRoutesOptions { ApiKey = "test-key" });
        var handler = new StubHttpMessageHandler((_, _) =>
            new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent("{}") });
        var factory = new SingleClientHttpClientFactory("google-routes", new HttpClient(handler));
        var client = new GoogleRoutesClient(factory, options, NullLogger<GoogleRoutesClient>.Instance);

        var result = await client.ComputeRouteAsync(Waypoints(2));

        Assert.Null(result);
    }

    [Fact]
    public async Task Parses_successful_response()
    {
        var options = Options.Create(new GoogleRoutesOptions { ApiKey = "test-key" });
        const string body = """
        { "routes": [ { "polyline": { "encodedPolyline": "abc123" }, "distanceMeters": 4210, "duration": "780s" } ] }
        """;
        var handler = new StubHttpMessageHandler((_, _) =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
        var factory = new SingleClientHttpClientFactory("google-routes", new HttpClient(handler));
        var client = new GoogleRoutesClient(factory, options, NullLogger<GoogleRoutesClient>.Instance);

        var result = await client.ComputeRouteAsync(Waypoints(2));

        Assert.NotNull(result);
        Assert.Equal("abc123", result!.EncodedPolyline);
        Assert.Equal(4210, result.DistanceMeters);
        Assert.Equal(780, result.DurationSeconds);
    }

    [Fact]
    public async Task Batches_more_than_25_waypoints_preserving_order()
    {
        var options = Options.Create(new GoogleRoutesOptions { ApiKey = "test-key" });
        var waypoints = Waypoints(30);
        var capturedBodies = new List<string>();
        var handler = new StubHttpMessageHandler((req, body) =>
        {
            capturedBodies.Add(body);
            // Every batch must contain <= 25 waypoints total (origin+destination+intermediates).
            Assert.True(body.Split("\"latitude\"").Length - 1 <= 25);
            const string respBody = """
            { "routes": [ { "polyline": { "encodedPolyline": "seg" }, "distanceMeters": 100, "duration": "10s" } ] }
            """;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(respBody) };
        });
        var factory = new SingleClientHttpClientFactory("google-routes", new HttpClient(handler));
        var client = new GoogleRoutesClient(factory, options, NullLogger<GoogleRoutesClient>.Instance);

        var result = await client.ComputeRouteAsync(waypoints);

        Assert.NotNull(result);
        Assert.Equal(2, capturedBodies.Count);
        Assert.Equal(200, result!.DistanceMeters); // 100 + 100 from two batches
        Assert.Equal(20, result.DurationSeconds);  // 10 + 10 from two batches

        // Batch 0 = waypoints[0..24] (25 points: indices 0-24).
        AssertBatchWaypoints(capturedBodies[0], waypoints.Take(25).ToList());
        // Batch 1 = waypoints[24..29] (6 points: indices 24-29). Index 24 is the seam point,
        // repeated from the end of batch 0 as the start of batch 1 (same coordinate value).
        AssertBatchWaypoints(capturedBodies[1], waypoints.Skip(24).Take(6).ToList());
    }

    /// Parses a computeRoutes request body and asserts its origin/intermediates/destination
    /// waypoints match `expected`, in order — catches reordering or seam-overlap bugs that
    /// only checking request count or aggregate totals would miss.
    static void AssertBatchWaypoints(string requestBody, IReadOnlyList<RouteWaypoint> expected)
    {
        using var doc = JsonDocument.Parse(requestBody);
        var root = doc.RootElement;
        var actual = new List<RouteWaypoint>();

        var originLatLng = root.GetProperty("origin").GetProperty("location").GetProperty("latLng");
        actual.Add(new RouteWaypoint(
            originLatLng.GetProperty("latitude").GetDouble(), originLatLng.GetProperty("longitude").GetDouble()));

        foreach (var intermediate in root.GetProperty("intermediates").EnumerateArray())
        {
            var latLng = intermediate.GetProperty("location").GetProperty("latLng");
            actual.Add(new RouteWaypoint(
                latLng.GetProperty("latitude").GetDouble(), latLng.GetProperty("longitude").GetDouble()));
        }

        var destLatLng = root.GetProperty("destination").GetProperty("location").GetProperty("latLng");
        actual.Add(new RouteWaypoint(
            destLatLng.GetProperty("latitude").GetDouble(), destLatLng.GetProperty("longitude").GetDouble()));

        Assert.Equal(expected.Count, actual.Count);
        for (var i = 0; i < expected.Count; i++)
        {
            Assert.Equal(expected[i].Lat, actual[i].Lat, precision: 9);
            Assert.Equal(expected[i].Lng, actual[i].Lng, precision: 9);
        }
    }

    [Fact]
    public async Task Returns_null_when_request_times_out()
    {
        // TimeoutSeconds = 0 makes the linked CancellationTokenSource cancel effectively
        // immediately. The fake handler delays far longer than that, so if the client's
        // per-call timeout works, ComputeRouteAsync returns null instead of throwing or
        // hanging on the (never-completing) response.
        var options = Options.Create(new GoogleRoutesOptions { ApiKey = "test-key", TimeoutSeconds = 0 });
        var handler = new DelayedHttpMessageHandler(TimeSpan.FromSeconds(5));
        var factory = new SingleClientHttpClientFactory("google-routes", new HttpClient(handler));
        var client = new GoogleRoutesClient(factory, options, NullLogger<GoogleRoutesClient>.Instance);

        var result = await client.ComputeRouteAsync(Waypoints(2));

        Assert.Null(result);
    }

    [Fact]
    public async Task Returns_null_when_response_missing_polyline()
    {
        // Regression test: a 200 response whose routes[0] is missing polyline/encodedPolyline
        // must not throw (KeyNotFoundException via GetProperty) — it must return null.
        var options = Options.Create(new GoogleRoutesOptions { ApiKey = "test-key" });
        const string body = """
        { "routes": [ { "distanceMeters": 4210, "duration": "780s" } ] }
        """;
        var handler = new StubHttpMessageHandler((_, _) =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
        var factory = new SingleClientHttpClientFactory("google-routes", new HttpClient(handler));
        var client = new GoogleRoutesClient(factory, options, NullLogger<GoogleRoutesClient>.Instance);

        var result = await client.ComputeRouteAsync(Waypoints(2));

        Assert.Null(result);
    }
}

/// Fake HttpMessageHandler that never completes within a short client-side timeout — used to
/// exercise the CancellationTokenSource-based per-call timeout path.
sealed class DelayedHttpMessageHandler(TimeSpan delay) : HttpMessageHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        await Task.Delay(delay, ct);
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") };
    }
}

/// Minimal fake HttpMessageHandler capturing the request body for assertions.
sealed class StubHttpMessageHandler(Func<HttpRequestMessage, string, HttpResponseMessage> respond) : HttpMessageHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
        return respond(request, body);
    }
}

/// Minimal fake IHttpClientFactory that always returns the same pre-built HttpClient,
/// matching how GoogleRoutesClient requests it by name ("google-routes").
sealed class SingleClientHttpClientFactory(string expectedName, HttpClient client) : IHttpClientFactory
{
    public HttpClient CreateClient(string name)
    {
        Assert.Equal(expectedName, name);
        return client;
    }
}
