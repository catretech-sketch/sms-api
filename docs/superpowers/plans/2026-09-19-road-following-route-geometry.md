# Road-Following Route Geometry (Backend) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a canonical, cached, road-following route geometry endpoint (`GET /v1/transport/routes/{routeId}/geometry`) backed by the Google Routes API, so every consumer app can stop drawing straight-line polylines between stops.

**Architecture:** New `RouteGeometries` SQL table (one row per route, keyed by an ordered-stop-hash) fed by a new `RouteGeometryService` that checks the hash, reuses cached geometry on a match, and calls Google Routes on a miss/failure with stale-cache-preferred degradation. A new, separately-authorized `RouteGeometryController` exposes it to any authorized viewer (not just Principal-role admins), reusing a new `CanViewRouteAsync` method on the existing `ITransportAuthorizationResolver`.

**Tech Stack:** ASP.NET Core (net10.0), Dapper/raw SQL (no EF Core), FluentMigrator, SQL Server Row-Level Security, xUnit.

**Spec:** `docs/superpowers/specs/2026-09-19-road-following-route-geometry-design.md`

## Global Constraints

- No existing endpoint, DTO, migration, hub, or worker changes shape — this feature is purely additive.
- Live GPS ingestion, SignalR (`TransportFleetHub`, `TransportFleetBroadcaster`), `TripRepository`, `BusTrackingStatusRules`, and ETA calculation are never touched by any task in this plan.
- The Google Routes API key must never be logged, returned in any response, or reachable from any frontend — server-side HTTP call only, via `IHttpClientFactory.CreateClient("google-routes")`.
- When geometry is unavailable, the API must return `status: "unavailable", geometry: null` — never a fabricated or straight-line geometry value.
- Every new table gets the same tenant RLS policy pattern already used by `TransportRoutes`/`RouteStops`/`BusParentAlerts`.

---

### Task 1: `RouteGeometries` migration

**Files:**
- Create: `db/Sms.Migrations/M0207_RouteGeometries.cs`
- Test: `tests/Sms.Tests.Integration/Transport/RouteGeometryMigrationTests.cs`

**Interfaces:**
- Produces: SQL table `dbo.RouteGeometries(RouteId, TenantId, StopSequenceHash, Format, EncodedPolyline, DistanceMeters, DurationSeconds, Provider, GeneratedAt)`, RLS policy `rls.RouteGeometriesTenantPolicy`.

- [ ] **Step 1: Write the migration**

```csharp
using FluentMigrator;

namespace Sms.Migrations;

[Migration(207, "RouteGeometries: cached road-following geometry per transport route")]
public sealed class M0207_RouteGeometries : Migration
{
    public override void Up()
    {
        Create.Table("RouteGeometries")
            .WithColumn("RouteId").AsGuid().PrimaryKey()
            .WithColumn("TenantId").AsGuid().NotNullable()
            .WithColumn("StopSequenceHash").AsString(64).NotNullable()
            .WithColumn("Format").AsString(32).NotNullable()
            .WithColumn("EncodedPolyline").AsCustom("nvarchar(max)").NotNullable()
            .WithColumn("DistanceMeters").AsInt32().NotNullable()
            .WithColumn("DurationSeconds").AsInt32().NotNullable()
            .WithColumn("Provider").AsString(32).NotNullable()
            .WithColumn("GeneratedAt").AsDateTime2().NotNullable();

        Execute.Sql(@"
CREATE SECURITY POLICY rls.RouteGeometriesTenantPolicy
ADD FILTER PREDICATE rls.fn_tenant_predicate(TenantId) ON dbo.RouteGeometries,
ADD BLOCK PREDICATE rls.fn_tenant_predicate(TenantId) ON dbo.RouteGeometries AFTER INSERT
WITH (STATE = ON);");
    }

    public override void Down()
    {
        Execute.Sql("DROP SECURITY POLICY IF EXISTS rls.RouteGeometriesTenantPolicy;");
        Delete.Table("RouteGeometries");
    }
}
```

- [ ] **Step 2: Write the migration-applies test**

```csharp
using Dapper;
using Xunit;

namespace Sms.Tests.Integration.Transport;

public class RouteGeometryMigrationTests : IntegrationTestBase
{
    [Fact]
    public async Task RouteGeometries_table_exists_with_expected_columns()
    {
        await using var conn = await OpenAdminConnectionAsync();
        var columns = (await conn.QueryAsync<string>(
            "SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'RouteGeometries'")).ToHashSet();

        Assert.Contains("RouteId", columns);
        Assert.Contains("TenantId", columns);
        Assert.Contains("StopSequenceHash", columns);
        Assert.Contains("EncodedPolyline", columns);
        Assert.Contains("DistanceMeters", columns);
        Assert.Contains("DurationSeconds", columns);
        Assert.Contains("GeneratedAt", columns);
    }
}
```

Check `tests/Sms.Tests.Integration/` for the actual base-class name/connection-opening helper used by existing tests (e.g. open `TransportTripsTests.cs` and copy its exact setup pattern — `IntegrationTestBase`/`OpenAdminConnectionAsync` above are placeholders for whatever that file actually calls) before finalizing this test.

- [ ] **Step 3: Run the migration against the test database and run the test**

Run: `dotnet run --project db/Sms.Migrations -- migrate` (or whatever the existing `MigrateCli.cs` entrypoint command is — check `db/Sms.Migrations/MigrateCli.cs` usage before running), then `dotnet test tests/Sms.Tests.Integration --filter RouteGeometryMigrationTests`
Expected: PASS

- [ ] **Step 4: Commit**

```bash
git add db/Sms.Migrations/M0207_RouteGeometries.cs tests/Sms.Tests.Integration/Transport/RouteGeometryMigrationTests.cs
git commit -m "feat(transport): add RouteGeometries table for cached road-following geometry"
```

---

### Task 2: Stop-sequence hash (pure logic)

**Files:**
- Create: `src/Sms.Modules.Transport/RouteGeometryHasher.cs`
- Test: `tests/Sms.Tests.Unit/Transport/RouteGeometryHasherTests.cs`

**Interfaces:**
- Consumes: `RouteStopListItem(Guid Id, Guid RouteId, string Name, int Sequence, double Lat, double Lng)` (existing, `BusModule.cs:40`).
- Produces: `static string RouteGeometryHasher.Compute(IReadOnlyList<RouteStopListItem> orderedStops)` — a lowercase hex SHA-256 string. Callers must pass stops already ordered by `Sequence` (as returned by `BusRepository.ListRouteStopsAsync`, which already orders by `Seq`).

- [ ] **Step 1: Write the failing tests**

```csharp
using Sms.Modules.Transport;
using Xunit;

namespace Sms.Tests.Unit.Transport;

public class RouteGeometryHasherTests
{
    static RouteStopListItem Stop(Guid id, int seq, double lat, double lng) =>
        new(id, Guid.NewGuid(), "Stop", seq, lat, lng);

    [Fact]
    public void Same_input_produces_same_hash()
    {
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        var stops = new[] { Stop(id1, 1, 12.1, 77.1), Stop(id2, 2, 12.2, 77.2) };

        var h1 = RouteGeometryHasher.Compute(stops);
        var h2 = RouteGeometryHasher.Compute(stops);

        Assert.Equal(h1, h2);
    }

    [Fact]
    public void Reordering_stops_changes_hash()
    {
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        var original = new[] { Stop(id1, 1, 12.1, 77.1), Stop(id2, 2, 12.2, 77.2) };
        var reordered = new[] { Stop(id2, 1, 12.2, 77.2), Stop(id1, 2, 12.1, 77.1) };

        Assert.NotEqual(RouteGeometryHasher.Compute(original), RouteGeometryHasher.Compute(reordered));
    }

    [Fact]
    public void Changing_a_coordinate_changes_hash()
    {
        var id1 = Guid.NewGuid();
        var before = new[] { Stop(id1, 1, 12.1, 77.1) };
        var after = new[] { Stop(id1, 1, 12.10001, 77.1) };

        Assert.NotEqual(RouteGeometryHasher.Compute(before), RouteGeometryHasher.Compute(after));
    }

    [Fact]
    public void Adding_a_stop_changes_hash()
    {
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        var before = new[] { Stop(id1, 1, 12.1, 77.1) };
        var after = new[] { Stop(id1, 1, 12.1, 77.1), Stop(id2, 2, 12.2, 77.2) };

        Assert.NotEqual(RouteGeometryHasher.Compute(before), RouteGeometryHasher.Compute(after));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Sms.Tests.Unit --filter RouteGeometryHasherTests`
Expected: FAIL (compile error — `RouteGeometryHasher` does not exist)

- [ ] **Step 3: Write the implementation**

```csharp
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Sms.Modules.Transport;

public static class RouteGeometryHasher
{
    /// Deterministic hash over an ordered stop list. Any add/remove/reorder/coordinate
    /// change to `RouteStops` for a route changes this value, which is how
    /// RouteGeometryService decides whether cached geometry can still be reused.
    public static string Compute(IReadOnlyList<RouteStopListItem> orderedStops)
    {
        var sb = new StringBuilder();
        foreach (var s in orderedStops)
        {
            sb.Append(s.Id).Append('|')
              .Append(s.Sequence).Append('|')
              .Append(s.Lat.ToString("R", CultureInfo.InvariantCulture)).Append('|')
              .Append(s.Lng.ToString("R", CultureInfo.InvariantCulture)).Append(';');
        }
        var bytes = Encoding.UTF8.GetBytes(sb.ToString());
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Sms.Tests.Unit --filter RouteGeometryHasherTests`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/Sms.Modules.Transport/RouteGeometryHasher.cs tests/Sms.Tests.Unit/Transport/RouteGeometryHasherTests.cs
git commit -m "feat(transport): add deterministic stop-sequence hash for route geometry caching"
```

---

### Task 3: Google Routes client + options

**Files:**
- Create: `src/Sms.Shared.Kernel/Routing/GoogleRoutesOptions.cs`
- Create: `src/Sms.Shared.Kernel/Routing/IGoogleRoutesClient.cs`
- Create: `src/Sms.Shared.Kernel/Routing/GoogleRoutesClient.cs`
- Test: `tests/Sms.Tests.Unit/Routing/GoogleRoutesClientTests.cs`

**Interfaces:**
- Produces:
  ```csharp
  public sealed record RouteWaypoint(double Lat, double Lng);
  public sealed record ComputedRouteGeometry(string EncodedPolyline, int DistanceMeters, int DurationSeconds);
  public interface IGoogleRoutesClient
  {
      Task<ComputedRouteGeometry?> ComputeRouteAsync(IReadOnlyList<RouteWaypoint> orderedWaypoints, CancellationToken ct = default);
  }
  ```
  Returns `null` on any failure (non-2xx, timeout, malformed body) — callers (Task 5) treat `null` as "provider unavailable," never as an exception to propagate.

- [ ] **Step 1: Write `GoogleRoutesOptions`**

```csharp
namespace Sms.Shared.Kernel.Routing;

public sealed class GoogleRoutesOptions
{
    public const string SectionName = "GoogleRoutes";
    public string ApiKey { get; set; } = "";
    public string BaseUrl { get; set; } = "https://routes.googleapis.com";
    public int TimeoutSeconds { get; set; } = 8;
    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey);
}
```

- [ ] **Step 2: Write `IGoogleRoutesClient` and the waypoint/result records**

```csharp
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
```

- [ ] **Step 3: Write the failing batching/order test first**

```csharp
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
```

- [ ] **Step 4: Run tests to verify they fail**

Run: `dotnet test tests/Sms.Tests.Unit --filter GoogleRoutesClientTests`
Expected: FAIL (compile error — `GoogleRoutesClient` does not exist)

- [ ] **Step 5: Write `GoogleRoutesClient`**

```csharp
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Sms.Shared.Kernel.Routing;

/// Server-side-only client for Google's Routes API (`computeRoutes`). Batches waypoints
/// in groups of <=25 (Google's per-request limit: origin + destination + up to 23
/// intermediates) and stitches results back together in the caller's original order.
/// Never throws for provider failures — returns null so callers degrade gracefully.
public sealed class GoogleRoutesClient(
    IHttpClientFactory httpClientFactory, IOptions<GoogleRoutesOptions> options, ILogger<GoogleRoutesClient> log)
    : IGoogleRoutesClient
{
    const int MaxWaypointsPerRequest = 25;

    public async Task<ComputedRouteGeometry?> ComputeRouteAsync(
        IReadOnlyList<RouteWaypoint> orderedWaypoints, CancellationToken ct = default)
    {
        var opts = options.Value;
        if (!opts.IsConfigured || orderedWaypoints.Count < 2)
            return null;

        var batches = BatchWaypoints(orderedWaypoints, MaxWaypointsPerRequest);
        var polylines = new List<string>();
        var totalDistance = 0;
        var totalDuration = 0;

        foreach (var batch in batches)
        {
            var segment = await ComputeSegmentAsync(batch, opts, ct);
            if (segment is null)
                return null; // any failing segment makes the whole route unavailable — never a partial fake route
            polylines.Add(segment.EncodedPolyline);
            totalDistance += segment.DistanceMeters;
            totalDuration += segment.DurationSeconds;
        }

        return new ComputedRouteGeometry(string.Join("", polylines), totalDistance, totalDuration);
    }

    /// Splits waypoints into <=maxPerRequest batches, repeating the last stop of batch N as
    /// the first stop of batch N+1 so consecutive segments share a junction point and there
    /// is no routing gap at the seam. Order is never altered.
    static List<List<RouteWaypoint>> BatchWaypoints(IReadOnlyList<RouteWaypoint> waypoints, int maxPerRequest)
    {
        var batches = new List<List<RouteWaypoint>>();
        var i = 0;
        while (i < waypoints.Count - 1)
        {
            var take = Math.Min(maxPerRequest, waypoints.Count - i);
            batches.Add(waypoints.Skip(i).Take(take).ToList());
            i += take - 1; // overlap by one point
        }
        return batches;
    }

    async Task<ComputedRouteGeometry?> ComputeSegmentAsync(
        IReadOnlyList<RouteWaypoint> waypoints, GoogleRoutesOptions opts, CancellationToken ct)
    {
        try
        {
            var client = httpClientFactory.CreateClient("google-routes");
            client.Timeout = TimeSpan.FromSeconds(opts.TimeoutSeconds);

            var payload = new
            {
                origin = new { location = new { latLng = new { latitude = waypoints[0].Lat, longitude = waypoints[0].Lng } } },
                destination = new { location = new { latLng = new { latitude = waypoints[^1].Lat, longitude = waypoints[^1].Lng } } },
                intermediates = waypoints.Skip(1).Take(waypoints.Count - 2)
                    .Select(w => new { location = new { latLng = new { latitude = w.Lat, longitude = w.Lng } } }),
                travelMode = "DRIVE",
            };

            using var req = new HttpRequestMessage(HttpMethod.Post, $"{opts.BaseUrl}/directions/v2:computeRoutes");
            req.Headers.Add("X-Goog-Api-Key", opts.ApiKey);
            req.Headers.Add("X-Goog-FieldMask", "routes.polyline.encodedPolyline,routes.distanceMeters,routes.duration");
            req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

            using var res = await client.SendAsync(req, ct);
            var body = await res.Content.ReadAsStringAsync(ct);
            if (!res.IsSuccessStatusCode)
            {
                log.LogWarning("Google Routes computeRoutes failed: {Status} {Body}", (int)res.StatusCode, body);
                return null;
            }

            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("routes", out var routes) || routes.GetArrayLength() == 0)
                return null;
            var route = routes[0];
            var polyline = route.GetProperty("polyline").GetProperty("encodedPolyline").GetString();
            if (string.IsNullOrEmpty(polyline)) return null;
            var distanceMeters = route.TryGetProperty("distanceMeters", out var d) ? d.GetInt32() : 0;
            var durationSeconds = route.TryGetProperty("duration", out var dur)
                ? ParseSecondsSuffix(dur.GetString())
                : 0;

            return new ComputedRouteGeometry(polyline, distanceMeters, durationSeconds);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            log.LogWarning(ex, "Google Routes computeRoutes call threw");
            return null;
        }
    }

    static int ParseSecondsSuffix(string? value) =>
        value is not null && value.EndsWith('s') && int.TryParse(value[..^1], out var seconds) ? seconds : 0;
}
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test tests/Sms.Tests.Unit --filter GoogleRoutesClientTests`
Expected: PASS

- [ ] **Step 7: Commit**

```bash
git add src/Sms.Shared.Kernel/Routing/ tests/Sms.Tests.Unit/Routing/
git commit -m "feat(routing): add server-side Google Routes client with waypoint batching"
```

---

### Task 4: `RouteGeometries` DTOs + repository

**Files:**
- Create: `src/Sms.Modules.Transport/RouteGeometryModule.cs`
- Test: `tests/Sms.Tests.Integration/Transport/RouteGeometryRepositoryTests.cs`

**Interfaces:**
- Consumes: `IDbConnectionFactory`, `BaseRepository` (existing, `Sms.Shared.Kernel.Data`).
- Produces:
  ```csharp
  public sealed record RouteGeometryRow(
      Guid RouteId, string StopSequenceHash, string Format, string EncodedPolyline,
      int DistanceMeters, int DurationSeconds, string Provider, DateTime GeneratedAt);
  public sealed class RouteGeometryRepository(IDbConnectionFactory factory) : BaseRepository(factory)
  {
      Task<RouteGeometryRow?> GetAsync(Guid routeId, CancellationToken ct = default);
      Task UpsertAsync(Guid tenantId, Guid routeId, string stopSequenceHash, string format,
          string encodedPolyline, int distanceMeters, int durationSeconds, string provider,
          DateTime generatedAt, CancellationToken ct = default);
  }
  public static class RouteGeometryModuleExtensions
  {
      IServiceCollection AddRouteGeometryModule(this IServiceCollection services);
  }
  ```

- [ ] **Step 1: Write the failing repository test**

```csharp
using Sms.Modules.Transport;
using Xunit;

namespace Sms.Tests.Integration.Transport;

public class RouteGeometryRepositoryTests : IntegrationTestBase
{
    [Fact]
    public async Task GetAsync_returns_null_when_no_row_exists()
    {
        var repo = new RouteGeometryRepository(ConnectionFactory);

        var result = await repo.GetAsync(Guid.NewGuid());

        Assert.Null(result);
    }

    [Fact]
    public async Task UpsertAsync_then_GetAsync_round_trips_the_row()
    {
        var repo = new RouteGeometryRepository(ConnectionFactory);
        var tenantId = await SeedTenantAsync();
        var routeId = await SeedRouteAsync(tenantId);

        await repo.UpsertAsync(tenantId, routeId, "hash-1", "google-encoded-polyline",
            "abc123", 4210, 780, "google-routes", DateTime.UtcNow);

        var row = await repo.GetAsync(routeId);
        Assert.NotNull(row);
        Assert.Equal("hash-1", row!.StopSequenceHash);
        Assert.Equal("abc123", row.EncodedPolyline);
    }

    [Fact]
    public async Task UpsertAsync_replaces_existing_row_for_same_route()
    {
        var repo = new RouteGeometryRepository(ConnectionFactory);
        var tenantId = await SeedTenantAsync();
        var routeId = await SeedRouteAsync(tenantId);
        await repo.UpsertAsync(tenantId, routeId, "hash-1", "google-encoded-polyline", "abc", 100, 10, "google-routes", DateTime.UtcNow);

        await repo.UpsertAsync(tenantId, routeId, "hash-2", "google-encoded-polyline", "xyz", 200, 20, "google-routes", DateTime.UtcNow);

        var row = await repo.GetAsync(routeId);
        Assert.Equal("hash-2", row!.StopSequenceHash);
        Assert.Equal("xyz", row.EncodedPolyline);
    }
}
```

Check `tests/Sms.Tests.Integration/Transport/TransportTripsTests.cs` (or similar) for the actual `IntegrationTestBase`/`ConnectionFactory`/`SeedTenantAsync`/`SeedRouteAsync` helper names before finalizing — this task must reuse existing test-seeding helpers rather than inventing new ones with different names.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Sms.Tests.Integration --filter RouteGeometryRepositoryTests`
Expected: FAIL (compile error)

- [ ] **Step 3: Write `RouteGeometryModule.cs`**

```csharp
using Microsoft.Extensions.DependencyInjection;
using Sms.Shared.Kernel.Data;

namespace Sms.Modules.Transport;

public sealed record RouteGeometryRow(
    Guid RouteId, string StopSequenceHash, string Format, string EncodedPolyline,
    int DistanceMeters, int DurationSeconds, string Provider, DateTime GeneratedAt);

public sealed class RouteGeometryRepository(IDbConnectionFactory factory) : BaseRepository(factory)
{
    public async Task<RouteGeometryRow?> GetAsync(Guid routeId, CancellationToken ct = default) =>
        (await QueryInlineAsync<RouteGeometryRow>(
            @"SELECT RouteId, StopSequenceHash, Format, EncodedPolyline, DistanceMeters, DurationSeconds, Provider, GeneratedAt
              FROM dbo.RouteGeometries WHERE RouteId = @routeId",
            new { routeId }, ct)).FirstOrDefault();

    public Task UpsertAsync(
        Guid tenantId, Guid routeId, string stopSequenceHash, string format, string encodedPolyline,
        int distanceMeters, int durationSeconds, string provider, DateTime generatedAt, CancellationToken ct = default) =>
        ExecuteInlineAsync(
            @"MERGE dbo.RouteGeometries AS tgt
              USING (SELECT @routeId AS RouteId) AS src ON tgt.RouteId = src.RouteId
              WHEN MATCHED THEN UPDATE SET
                  StopSequenceHash = @stopSequenceHash, Format = @format, EncodedPolyline = @encodedPolyline,
                  DistanceMeters = @distanceMeters, DurationSeconds = @durationSeconds,
                  Provider = @provider, GeneratedAt = @generatedAt
              WHEN NOT MATCHED THEN INSERT
                  (RouteId, TenantId, StopSequenceHash, Format, EncodedPolyline, DistanceMeters, DurationSeconds, Provider, GeneratedAt)
                  VALUES (@routeId, @tenantId, @stopSequenceHash, @format, @encodedPolyline, @distanceMeters, @durationSeconds, @provider, @generatedAt);",
            new { routeId, tenantId, stopSequenceHash, format, encodedPolyline, distanceMeters, durationSeconds, provider, generatedAt },
            ct);
}

public static class RouteGeometryModuleExtensions
{
    public static IServiceCollection AddRouteGeometryModule(this IServiceCollection services) =>
        services.AddScoped<RouteGeometryRepository>();
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Sms.Tests.Integration --filter RouteGeometryRepositoryTests`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/Sms.Modules.Transport/RouteGeometryModule.cs tests/Sms.Tests.Integration/Transport/RouteGeometryRepositoryTests.cs
git commit -m "feat(transport): add RouteGeometryRepository for cached route geometry persistence"
```

---

### Task 5: `RouteGeometryService` orchestration

**Files:**
- Create: `src/Sms.Application/Services/Transport/RouteGeometryService.cs`
- Test: `tests/Sms.Tests.Unit/Transport/RouteGeometryServiceTests.cs`

**Interfaces:**
- Consumes: `BusRepository.ListRouteStopsAsync(Guid, CancellationToken)` (existing, returns stops ordered by `Seq`), `RouteGeometryRepository.GetAsync`/`UpsertAsync` (Task 4), `IGoogleRoutesClient.ComputeRouteAsync` (Task 3), `RouteGeometryHasher.Compute` (Task 2).
- Produces:
  ```csharp
  public enum RouteGeometryStatus { Available, Unavailable }
  public sealed record RouteGeometryResult(
      Guid RouteId, RouteGeometryStatus Status, string? Format, string? Geometry,
      int? DistanceMeters, int? DurationSeconds, string StopSequenceHash, DateTime? GeneratedAt);
  public interface IRouteGeometryService
  {
      Task<RouteGeometryResult> GetAsync(Guid tenantId, Guid routeId, CancellationToken ct = default);
  }
  ```

- [ ] **Step 1: Write the failing tests**

```csharp
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
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Sms.Tests.Unit --filter RouteGeometryServiceTests`
Expected: FAIL (compile error — `RouteGeometryService`, `IRouteStopSource`, `IRouteGeometryStore` do not exist yet)

- [ ] **Step 3: Write the implementation**

Two small seam interfaces (`IRouteStopSource`, `IRouteGeometryStore`) are introduced purely so this service is unit-testable without a real DB connection — `BusRepository` and `RouteGeometryRepository` each already satisfy one of them structurally, so no wrapper classes are needed in production, only explicit interface declarations added to those two existing/new classes.

```csharp
using Sms.Modules.Transport;
using Sms.Shared.Kernel.Routing;

namespace Sms.Application.Services.Transport;

public interface IRouteStopSource
{
    Task<IReadOnlyList<RouteStopListItem>> ListRouteStopsAsync(Guid routeId, CancellationToken ct = default);
}

public interface IRouteGeometryStore
{
    Task<RouteGeometryRow?> GetAsync(Guid routeId, CancellationToken ct = default);
    Task UpsertAsync(Guid tenantId, Guid routeId, string stopSequenceHash, string format, string encodedPolyline,
        int distanceMeters, int durationSeconds, string provider, DateTime generatedAt, CancellationToken ct = default);
}

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
```

Then add `: IRouteStopSource` to `BusRepository`'s class declaration in `BusModule.cs` (it already has a matching `ListRouteStopsAsync(Guid, CancellationToken)` method — no method body changes needed) and `: IRouteGeometryStore` to `RouteGeometryRepository`'s declaration from Task 4 (same reasoning).

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Sms.Tests.Unit --filter RouteGeometryServiceTests`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/Sms.Application/Services/Transport/RouteGeometryService.cs src/Sms.Modules.Transport/BusModule.cs src/Sms.Modules.Transport/RouteGeometryModule.cs tests/Sms.Tests.Unit/Transport/RouteGeometryServiceTests.cs
git commit -m "feat(transport): add RouteGeometryService cache/regenerate/stale-fallback orchestration"
```

---

### Task 6: `CanViewRouteAsync` authorization extension

**Files:**
- Modify: `src/Sms.Application/Services/Transport/ITransportAuthorizationResolver.cs`
- Modify: `src/Sms.Application/Services/Transport/TransportAuthorizationResolver.cs`
- Modify: `src/Sms.Modules.Transport/BusModule.cs` (add two small query methods to `BusRepository`)
- Test: `tests/Sms.Tests.Unit/Transport/TransportAuthorizationResolverRouteTests.cs` (extend existing authorization test file if one already covers `CanViewBusAsync` — check `tests/Sms.Tests.Unit/Transport/` and `tests/Sms.Tests.Integration/Transport/` for an existing `TransportAuthorizationResolverTests.cs` first and add to it instead of creating a parallel file)

**Interfaces:**
- Consumes: `CanViewBusAsync` (existing, same class), `BusRepository` (existing).
- Produces: `Task<bool> CanViewRouteAsync(Guid callerUserId, Guid callerTenantId, IReadOnlyCollection<string> callerRoles, Guid routeId, CancellationToken ct = default)` on `ITransportAuthorizationResolver`.

- [ ] **Step 1: Add the two small repository queries to `BusRepository`** (in `BusModule.cs`, alongside the other `RouteStop*`/`Route*` methods)

```csharp
public async Task<bool> RouteExistsAsync(Guid routeId, CancellationToken ct = default) =>
    (await QueryInlineAsync<int>("SELECT COUNT(1) FROM dbo.TransportRoutes WHERE Id = @routeId", new { routeId }, ct)).First() > 0;

public async Task<IReadOnlyList<Guid>> ListBusIdsForRouteAsync(Guid routeId, CancellationToken ct = default) =>
    await QueryInlineAsync<Guid>("SELECT Id FROM dbo.Buses WHERE RouteId = @routeId", new { routeId }, ct);
```

- [ ] **Step 2: Write the failing authorization tests**

```csharp
// Add to (or create, matching whatever existing test class already covers CanViewBusAsync):
// tests/Sms.Tests.Unit/Transport/TransportAuthorizationResolverTests.cs
//
// These tests assume the existing test file already has fakes/mocks for TripRepository,
// BusRepository, StudentBusRepository, IAuthDao, ITenantContext used by CanViewBusAsync's
// existing tests — reuse those exact fakes rather than inventing new ones.

[Fact]
public async Task CanViewRouteAsync_admin_sees_route_that_exists_in_their_tenant()
{
    // Arrange: BusRepository fake/mock returns true for RouteExistsAsync(routeId).
    // Act: resolver.CanViewRouteAsync(adminUserId, tenantId, [Policies.Principal], routeId)
    // Assert: true
}

[Fact]
public async Task CanViewRouteAsync_teacher_sees_route_via_bus_they_have_duty_on()
{
    // Arrange: BusRepository fake returns [busId] for ListBusIdsForRouteAsync(routeId),
    // and IsDutyTeacherForBusAsync(teacherUserId, busId) returns true (existing method
    // CanViewBusAsync already relies on).
    // Act: resolver.CanViewRouteAsync(teacherUserId, tenantId, [Policies.Teacher], routeId)
    // Assert: true
}

[Fact]
public async Task CanViewRouteAsync_returns_false_when_caller_has_no_relationship_to_any_bus_on_the_route()
{
    // Arrange: ListBusIdsForRouteAsync returns [busId], but every per-bus check on
    // CanViewBusAsync's path returns false for this caller.
    // Act / Assert: false
}
```

Fill in the exact mock/fake setup calls once the existing `CanViewBusAsync` test file's mocking style (Moq? NSubstitute? hand-written fakes?) is confirmed by reading it — do not invent a different mocking approach for this new method.

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test tests/Sms.Tests.Unit --filter TransportAuthorizationResolver`
Expected: FAIL (compile error — `CanViewRouteAsync` does not exist)

- [ ] **Step 4: Extend the interface**

```csharp
namespace Sms.Application.Services.Transport;

public interface ITransportAuthorizationResolver
{
    Task<bool> CanViewBusAsync(Guid callerUserId, Guid callerTenantId, IReadOnlyCollection<string> callerRoles, Guid busId, CancellationToken ct = default);

    /// Route-level equivalent of CanViewBusAsync, used by the road-following route
    /// geometry endpoint. A route is visible to a caller if any bus currently assigned
    /// to that route would itself be visible to them under CanViewBusAsync.
    Task<bool> CanViewRouteAsync(Guid callerUserId, Guid callerTenantId, IReadOnlyCollection<string> callerRoles, Guid routeId, CancellationToken ct = default);
}
```

- [ ] **Step 5: Implement in `TransportAuthorizationResolver`**

```csharp
public async Task<bool> CanViewRouteAsync(
    Guid callerUserId, Guid callerTenantId, IReadOnlyCollection<string> callerRoles,
    Guid routeId, CancellationToken ct = default)
{
    tenant.Set(callerTenantId, callerUserId, isPlatform: false);

    if (callerRoles.Contains(Policies.Principal) || callerRoles.Contains(Policies.SchoolAdmin) || callerRoles.Contains(Policies.SchoolOwner))
    {
        if (await buses.RouteExistsAsync(routeId, ct)) return true;
    }

    var busIds = await buses.ListBusIdsForRouteAsync(routeId, ct);
    foreach (var busId in busIds)
        if (await CanViewBusAsync(callerUserId, callerTenantId, callerRoles, busId, ct))
            return true;

    return false;
}
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test tests/Sms.Tests.Unit --filter TransportAuthorizationResolver`
Expected: PASS

- [ ] **Step 7: Commit**

```bash
git add src/Sms.Application/Services/Transport/ITransportAuthorizationResolver.cs src/Sms.Application/Services/Transport/TransportAuthorizationResolver.cs src/Sms.Modules.Transport/BusModule.cs tests/Sms.Tests.Unit/Transport/
git commit -m "feat(transport): add per-route authorization check reusing existing per-bus rules"
```

---

### Task 7: `RouteGeometryController` (separately authorized)

**Files:**
- Create: `src/Sms.Api/Controllers/RouteGeometryController.cs`
- Test: `tests/Sms.Tests.Integration/Transport/RouteGeometryControllerTests.cs`

**Why a separate controller, not a new action on `TransportController`:** `TransportController` carries `[Authorize(Policy = Policies.Principal)]` at the class level. ASP.NET Core combines a class-level policy with any action-level `[Authorize]` using AND semantics — it does not let a looser action-level attribute override a stricter class-level one. Since teachers/parents/drivers are not Principals, the geometry action must live in its own controller with its own, looser, class-level `[Authorize]`, following the exact precedent already in this codebase: `ParentTransportController` (`[Route("v1/me")]`, `[Authorize(Policy = Policies.StudentOrParent)]`) is a second controller under a shared `v1/...` prefix for exactly this reason.

**Interfaces:**
- Consumes: `IRouteGeometryService.GetAsync` (Task 5), `ITransportAuthorizationResolver.CanViewRouteAsync` (Task 6), `ApiControllerBase.ForbiddenResult`/`OkData` (existing).

- [ ] **Step 1: Write the failing integration test**

```csharp
using Xunit;

namespace Sms.Tests.Integration.Transport;

public class RouteGeometryControllerTests : IntegrationTestBase
{
    [Fact]
    public async Task Principal_gets_available_geometry_for_own_tenant_route()
    {
        // Arrange: seed a tenant, a route with 2 stops, a fake IGoogleRoutesClient (via test DI
        // override) returning a fixed ComputedRouteGeometry, and a Principal-role auth token.
        // Act: GET /v1/transport/routes/{routeId}/geometry with that token.
        // Assert: 200, body.data.status == "available", body.data.geometry == the fixed polyline.
    }

    [Fact]
    public async Task Teacher_without_bus_duty_on_the_route_gets_403()
    {
        // Arrange: seed a route with a bus the caller has no duty/assignment relationship to,
        // and a Teacher-role auth token for an unrelated teacher.
        // Act: GET /v1/transport/routes/{routeId}/geometry.
        // Assert: 403.
    }

    [Fact]
    public async Task Route_belonging_to_another_tenant_is_not_visible()
    {
        // Arrange: seed routeId under tenant A; authenticate as a Principal of tenant B.
        // Act: GET /v1/transport/routes/{routeId}/geometry.
        // Assert: RLS makes RouteExistsAsync return false for tenant B's session -> 403 (not 404,
        // since the endpoint doesn't leak existence — matches the "no anonymous access, no cross-
        // tenant leak" requirement).
    }

    [Fact]
    public async Task No_stops_or_provider_unavailable_returns_200_with_unavailable_status()
    {
        // Arrange: seed a route with 0 stops.
        // Act / Assert: 200, body.data.status == "unavailable", body.data.geometry == null.
    }
}
```

Fill in the exact seeding/auth-token-issuing helper calls used by other integration tests in this file's sibling files (e.g. `TransportTripsTests.cs`) before finalizing — reuse them rather than inventing a new test-auth mechanism.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Sms.Tests.Integration --filter RouteGeometryControllerTests`
Expected: FAIL (404 — route/controller doesn't exist yet)

- [ ] **Step 3: Write the controller**

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sms.Application.Services.Transport;

namespace Sms.Api.Controllers;

public sealed record RouteGeometryResponse(
    Guid RouteId, string Status, string? Format, string? Geometry,
    int? DistanceMeters, int? DurationSeconds, string StopSequenceHash, DateTime? GeneratedAt);

/// Canonical road-following route geometry, shared by every authorized consumer app
/// (CRM/admin, teacher, parent/student, driver/staff) — one endpoint, one contract.
/// Deliberately its own controller (not an action on TransportController) so it can carry
/// a broader [Authorize] than TransportController's class-level Principal-only policy;
/// see the per-route CanViewRouteAsync check below for the real access control.
[Route("v1/transport/routes")]
[Authorize]
public sealed class RouteGeometryController(
    IRouteGeometryService geometry, ITransportAuthorizationResolver authz) : ApiControllerBase
{
    [HttpGet("{routeId:guid}/geometry")]
    public async Task<IActionResult> GetGeometry(Guid routeId, CancellationToken ct)
    {
        var tenantIdRaw = User.FindFirst("tenant_id")?.Value;
        var userIdRaw = User.FindFirst("sub")?.Value;
        if (tenantIdRaw is null || userIdRaw is null || !Guid.TryParse(tenantIdRaw, out var tenantId) || !Guid.TryParse(userIdRaw, out var userId))
            return ForbiddenResult("Missing tenant/user context.");

        var roles = User.FindAll("role").Select(c => c.Value).ToArray();
        if (!await authz.CanViewRouteAsync(userId, tenantId, roles, routeId, ct))
            return ForbiddenResult("Not authorized to view this route.");

        var result = await geometry.GetAsync(tenantId, routeId, ct);
        return OkData(new RouteGeometryResponse(
            result.RouteId, result.Status == RouteGeometryStatus.Available ? "available" : "unavailable",
            result.Format, result.Geometry, result.DistanceMeters, result.DurationSeconds,
            result.StopSequenceHash, result.GeneratedAt));
    }
}
```

- [ ] **Step 4: Wire DI (see Task 8 — done together since the controller won't resolve otherwise)**

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/Sms.Tests.Integration --filter RouteGeometryControllerTests`
Expected: PASS

- [ ] **Step 6: Commit**

```bash
git add src/Sms.Api/Controllers/RouteGeometryController.cs tests/Sms.Tests.Integration/Transport/RouteGeometryControllerTests.cs
git commit -m "feat(transport): add GET /v1/transport/routes/{routeId}/geometry endpoint"
```

---

### Task 8: DI wiring + configuration

**Files:**
- Modify: `src/Sms.Api/Extensions/ServiceCollectionExtensions.cs`
- Modify: `src/Sms.Api/appsettings.json`
- Modify: `src/Sms.Api/appsettings.Development.json`

**Interfaces:**
- Consumes: `GoogleRoutesOptions` (Task 3), `IGoogleRoutesClient`/`GoogleRoutesClient` (Task 3), `AddRouteGeometryModule()` (Task 4), `IRouteGeometryService`/`RouteGeometryService` (Task 5), `ITransportAuthorizationResolver` (Task 6, already registered — no change needed there).

- [ ] **Step 1: Add configuration sections**

In `appsettings.json` and `appsettings.Development.json`, add (matching the blank-in-source-control `AiSearch` convention exactly):

```json
"GoogleRoutes": {
  "ApiKey": "",
  "BaseUrl": "https://routes.googleapis.com"
}
```

- [ ] **Step 2: Register services**

In `ServiceCollectionExtensions.cs`, near the existing `AiSearchOptions`/`claude` HttpClient registration block (around line 140):

```csharp
builder.Services.Configure<GoogleRoutesOptions>(builder.Configuration.GetSection(GoogleRoutesOptions.SectionName));
builder.Services.AddHttpClient("google-routes");
builder.Services.AddSingleton<IGoogleRoutesClient, GoogleRoutesClient>();
builder.Services.AddRouteGeometryModule();
builder.Services.AddScoped<IRouteGeometryService, RouteGeometryService>();
```

Add `using Sms.Shared.Kernel.Routing;` to the file's using block alongside the existing `using Sms.Shared.Kernel.AiSearch;`.

Note: `RouteGeometryService`'s constructor takes `IRouteStopSource`, `IGoogleRoutesClient`, `IRouteGeometryStore` (Task 5) — since `BusRepository` and `RouteGeometryRepository` implement those interfaces directly (Task 5/6), no extra registration is needed for those two interfaces as long as `BusRepository` is already registered as itself elsewhere (confirm via existing DI registration for `BusRepository`) and `RouteGeometryRepository` is registered via `AddRouteGeometryModule()`. If DI resolution fails because `IRouteStopSource`/`IRouteGeometryStore` aren't separately registered, add:
```csharp
builder.Services.AddScoped<IRouteStopSource>(sp => sp.GetRequiredService<BusRepository>());
builder.Services.AddScoped<IRouteGeometryStore>(sp => sp.GetRequiredService<RouteGeometryRepository>());
```

- [ ] **Step 3: Verify the app boots and the endpoint resolves**

Run: `dotnet build` then start the API locally (or run the full integration suite, which exercises DI resolution): `dotnet test tests/Sms.Tests.Integration --filter RouteGeometry`
Expected: all `RouteGeometry*` integration tests from Tasks 1, 4, 7 PASS (this confirms DI wiring is correct end-to-end).

- [ ] **Step 4: Commit**

```bash
git add src/Sms.Api/Extensions/ServiceCollectionExtensions.cs src/Sms.Api/appsettings.json src/Sms.Api/appsettings.Development.json
git commit -m "feat(transport): wire up Google Routes client and route geometry service in DI"
```

---

### Task 9: Full regression pass

**Files:** none new — this task only runs the suite and inspects git state, per the mandatory git-safety requirement.

- [ ] **Step 1: Run the full backend test suite**

Run: `dotnet test`
Expected: PASS — including, unmodified, `tests/Sms.Tests.Integration/Transport/TransportTripsTests.cs`, `BusEtaTests.cs`, and every existing fleet/position/live-snapshot/authorization test. If any of these fail, STOP and investigate before proceeding — this plan must never ship alongside a regression in existing transport behavior.

- [ ] **Step 2: Confirm no unrelated files changed**

Run: `git status` and `git diff --stat`
Expected: only files listed in Tasks 1–8 appear modified/created; no changes to `TripRepository`, `TransportFleetHub`, `TransportFleetBroadcaster`, `BusTrackingStatusRules`, any ETA code, or any existing route/stop/fleet/trip DTO or endpoint.

- [ ] **Step 3: Record pre-existing vs. new changes**

If `git status` at the start of this work showed any pre-existing uncommitted changes in `sms-backend` unrelated to this feature, list them explicitly in the final report and confirm none were reset, reverted, or overwritten by any commit in Tasks 1–8.
