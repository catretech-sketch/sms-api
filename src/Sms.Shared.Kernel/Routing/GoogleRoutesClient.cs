using System.Text;
using System.Text.Json;
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

        // Google's Encoded Polyline Algorithm is delta-encoded — each point is an offset from the
        // previous one, and a string's first point is an offset from (0,0). Naively concatenating
        // encoded strings does NOT concatenate their point lists: the second string's first point
        // would decode as an offset from the first string's LAST point, corrupting every later
        // point. Decode each segment, drop the duplicated seam point that BatchWaypoints
        // deliberately introduced at each batch boundary (last point of batch N == first point of
        // batch N+1), then re-encode the merged point list into one valid polyline.
        var allPoints = new List<(double Lat, double Lng)>();
        for (var i = 0; i < polylines.Count; i++)
        {
            var points = PolylineCodec.Decode(polylines[i]);
            allPoints.AddRange(i == 0 ? points : points.Skip(1));
        }

        return new ComputedRouteGeometry(PolylineCodec.Encode(allPoints), totalDistance, totalDuration);
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

            // Per-call timeout via a linked CTS rather than mutating HttpClient.Timeout — the
            // client instance may be shared/reused (named-client pooling, or one call batching
            // multiple sequential requests), and HttpClient.Timeout cannot change once a request
            // on that instance has started.
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(opts.TimeoutSeconds));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

            using var res = await client.SendAsync(req, linkedCts.Token);
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
            if (!route.TryGetProperty("polyline", out var polylineElement) ||
                !polylineElement.TryGetProperty("encodedPolyline", out var encodedPolylineElement))
                return null;
            var polyline = encodedPolylineElement.GetString();
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

/// Google's Encoded Polyline Algorithm Format (both directions). Delta-encoded: each point is
/// stored as an offset from the previous point (the first point is an offset from (0,0)), with
/// each coordinate's delta zigzag-encoded into 5-bit chunks over a base64-like alphabet offset by
/// 63. See https://developers.google.com/maps/documentation/utilities/polylinealgorithm.
public static class PolylineCodec
{
    public static List<(double Lat, double Lng)> Decode(string encoded)
    {
        var points = new List<(double Lat, double Lng)>();
        var index = 0;
        long lat = 0, lng = 0;

        while (index < encoded.Length)
        {
            lat += DecodeNextDelta(encoded, ref index);
            lng += DecodeNextDelta(encoded, ref index);
            points.Add((lat / 1e5, lng / 1e5));
        }

        return points;
    }

    static long DecodeNextDelta(string encoded, ref int index)
    {
        long result = 0;
        var shift = 0;
        int chunk;
        do
        {
            chunk = encoded[index++] - 63;
            result |= (long)(chunk & 0x1f) << shift;
            shift += 5;
        } while (chunk >= 0x20);

        return (result & 1) != 0 ? ~(result >> 1) : result >> 1;
    }

    public static string Encode(IReadOnlyList<(double Lat, double Lng)> points)
    {
        var sb = new StringBuilder();
        long prevLat = 0, prevLng = 0;

        foreach (var (lat, lng) in points)
        {
            var curLat = (long)Math.Round(lat * 1e5);
            var curLng = (long)Math.Round(lng * 1e5);
            EncodeDelta(curLat - prevLat, sb);
            EncodeDelta(curLng - prevLng, sb);
            prevLat = curLat;
            prevLng = curLng;
        }

        return sb.ToString();
    }

    static void EncodeDelta(long delta, StringBuilder sb)
    {
        var value = delta < 0 ? ~(delta << 1) : delta << 1;
        while (value >= 0x20)
        {
            sb.Append((char)((0x20 | (value & 0x1f)) + 63));
            value >>= 5;
        }
        sb.Append((char)(value + 63));
    }
}
