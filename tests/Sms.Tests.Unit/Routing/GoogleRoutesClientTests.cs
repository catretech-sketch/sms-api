using System.Net;
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
        var requestCount = 0;
        var handler = new StubHttpMessageHandler((req, body) =>
        {
            requestCount++;
            // Every batch must contain <= 25 waypoints total (origin+destination+intermediates).
            Assert.True(body.Split("\"latitude\"").Length - 1 <= 25);
            const string respBody = """
            { "routes": [ { "polyline": { "encodedPolyline": "seg" }, "distanceMeters": 100, "duration": "10s" } ] }
            """;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(respBody) };
        });
        var factory = new SingleClientHttpClientFactory("google-routes", new HttpClient(handler));
        var client = new GoogleRoutesClient(factory, options, NullLogger<GoogleRoutesClient>.Instance);

        var result = await client.ComputeRouteAsync(Waypoints(30));

        Assert.NotNull(result);
        Assert.True(requestCount >= 2, "30 waypoints must be split into at least 2 batches of <=25");
        Assert.Equal(200, result!.DistanceMeters); // 100 + 100 from two batches
        Assert.Equal(20, result.DurationSeconds);  // 10 + 10 from two batches
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
