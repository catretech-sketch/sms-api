using Microsoft.Extensions.DependencyInjection;
using Sms.Shared.Kernel.Data;

namespace Sms.Modules.Transport;

public sealed record BusStopResponse(Guid Id, string Name, string? Time, int Seq, double Lat, double Lng);
public sealed record BusPositionResponse(Guid BusId, int CurrentStopIndex, double Progress, double? Lat, double? Lng, string? NextStopName, int? EtaMinutes);
public sealed record BusResponse(
    Guid Id, string BusNo, string? RouteName, string? Driver, string? DriverPhone, IReadOnlyList<BusStopResponse> Stops);
public sealed record BusRosterEntry(Guid StudentId, string StudentName, string Initials, Guid? StopId, string Status);
public sealed record BusBoardingItem(Guid StudentId, Guid? StopId, string Status, DateTime? At);
public sealed record BusBoardingRequest(IReadOnlyList<BusBoardingItem> Records);

/// Aggregate KPIs for the Operations · Transport dashboard, derived from the fleet master tables.
public sealed record TransportSummaryResponse(int Vehicles, int Routes, int Students, int Stops);

/// One row of the live fleet board (admin Live bus tracking screen).
public sealed record FleetBusResponse(
    Guid BusId, string BusNo, string? RouteName, string? Driver, string? DriverPhone,
    int StopCount, int StudentsRiding, string Status,
    double? Lat, double? Lng, double? SpeedKmh, string? NextStopName, DateTime? LastPingAt);

/// Raw per-bus fleet row before status / next-stop derivation.
public sealed record FleetBusRow(
    Guid BusId, string BusNo, string? RouteName, string? Driver, string? DriverPhone,
    int StopCount, Guid? TripId, double? Lat, double? Lng, double? SpeedKmh, DateTime? LastPingAt, int StudentsRiding);

public sealed class BusRepository(IDbConnectionFactory factory) : BaseRepository(factory)
{
    private sealed record BusRow(Guid Id, string BusNo, string? RouteName, string? Driver, string? DriverPhone);
    private sealed record RosterRow(Guid StudentId, string StudentName, Guid? StopId, string Status);

    public async Task<BusResponse?> GetAssignedAsync(Guid teacherUserId, CancellationToken ct = default)
    {
        var bus = (await QueryInlineAsync<BusRow>(
            @"SELECT TOP 1 b.Id, b.BusNo, b.RouteName, b.Driver, b.DriverPhone
              FROM dbo.Buses b JOIN dbo.BusAssignments a ON a.BusId = b.Id
              WHERE a.TeacherUserId = @teacherUserId", new { teacherUserId }, ct)).FirstOrDefault();
        if (bus is null) return null;
        var stops = await QueryInlineAsync<BusStopResponse>(
            "SELECT Id, Name, Time, Seq, Lat, Lng FROM dbo.BusStops WHERE BusId = @busId ORDER BY Seq",
            new { busId = bus.Id }, ct);
        return new BusResponse(bus.Id, bus.BusNo, bus.RouteName, bus.Driver, bus.DriverPhone, stops);
    }

    // The bus's current live trip, matched by BusNo (Transport trips carry BusNo, not BusId). Null if none live.
    private async Task<Guid?> CurrentTripIdAsync(Guid busId, CancellationToken ct) =>
        (await QueryInlineAsync<Guid>(
            @"SELECT TOP 1 t.Id FROM dbo.Trips t
              WHERE t.BusId = @busId AND t.Status = 'live' ORDER BY t.StartedAt DESC",
            new { busId }, ct)).Cast<Guid?>().FirstOrDefault();

    public async Task<IReadOnlyList<BusRosterEntry>> GetRosterAsync(Guid busId, CancellationToken ct = default)
    {
        var tripId = await CurrentTripIdAsync(busId, ct);
        if (tripId is null) return [];
        var rows = await QueryInlineAsync<RosterRow>(
            @"SELECT bo.StudentId, s.Name AS StudentName, bo.StopId, bo.State AS Status
              FROM dbo.Boardings bo JOIN dbo.Students s ON s.Id = bo.StudentId
              WHERE bo.TripId = @tripId ORDER BY s.Name", new { tripId }, ct);
        return rows.Select(r => new BusRosterEntry(r.StudentId, r.StudentName, Initials(r.StudentName), r.StopId, r.Status)).ToList();
    }

    /// Every bus with its current live trip (matched by BusNo), latest GPS ping and boarded count.
    public Task<IReadOnlyList<FleetBusRow>> FleetAsync(CancellationToken ct = default) =>
        QueryInlineAsync<FleetBusRow>(
            @"SELECT b.Id AS BusId, b.BusNo, b.RouteName, b.Driver, b.DriverPhone,
                (SELECT COUNT(*) FROM dbo.BusStops s WHERE s.BusId = b.Id) AS StopCount,
                t.Id AS TripId, p.Lat, p.Lng, p.SpeedKmh, p.At AS LastPingAt,
                ISNULL(bd.Cnt, 0) AS StudentsRiding
              FROM dbo.Buses b
              OUTER APPLY (
                SELECT TOP 1 tt.Id, tt.StartedAt FROM dbo.Trips tt
                WHERE tt.BusId = b.Id AND tt.Status = 'live' ORDER BY tt.StartedAt DESC) t
              OUTER APPLY (
                SELECT TOP 1 pp.Lat, pp.Lng, pp.SpeedKmh, pp.At FROM dbo.TripPings pp
                WHERE pp.TripId = t.Id ORDER BY pp.At DESC) p
              OUTER APPLY (
                SELECT COUNT(*) AS Cnt FROM dbo.Boardings bo
                WHERE bo.TripId = t.Id AND bo.State = 'boarded') bd
              ORDER BY b.BusNo", null, ct);

    /// Vehicles/Stops = row counts; Routes = distinct named routes; Students = distinct pupils who have boarded.
    public async Task<TransportSummaryResponse> SummaryAsync(CancellationToken ct = default) =>
        (await QueryInlineAsync<TransportSummaryResponse>(
            @"SELECT
                (SELECT COUNT(*) FROM dbo.Buses) AS Vehicles,
                (SELECT COUNT(DISTINCT RouteName) FROM dbo.Buses
                   WHERE RouteName IS NOT NULL AND LTRIM(RTRIM(RouteName)) <> '') AS Routes,
                (SELECT COUNT(DISTINCT StudentId) FROM dbo.Boardings) AS Students,
                (SELECT COUNT(*) FROM dbo.BusStops) AS Stops", null, ct)).First();

    public async Task<bool> UpsertBoardingAsync(
        Guid tenantId, Guid busId, IReadOnlyList<BusBoardingItem> records, DateTime now, CancellationToken ct = default)
    {
        var tripId = await CurrentTripIdAsync(busId, ct);
        if (tripId is null) return false;
        foreach (var r in records)
            await ExecuteProcAsync("dbo.Boarding_Upsert", new
            {
                TenantId = tenantId,
                TripId = tripId.Value,
                r.StudentId,
                r.StopId,
                State = r.Status,
                At = r.At ?? now
            }, ct);
        return true;
    }

    private sealed record StopRow(string Name, int Seq, double Lat, double Lng);
    // SpeedKmh is NOT NULL (default 0) on dbo.TripPings — "missing" reads as 0, not null.
    private sealed record PingRow2(double Lat, double Lng, double SpeedKmh);

    public async Task<BusPositionResponse> GetPositionAsync(Guid busId, CancellationToken ct = default)
    {
        var stops = await QueryInlineAsync<StopRow>(
            "SELECT Name, Seq, Lat, Lng FROM dbo.BusStops WHERE BusId = @busId ORDER BY Seq", new { busId }, ct);
        var tripId = await CurrentTripIdAsync(busId, ct);
        PingRow2? ping = tripId is null ? null : (await QueryInlineAsync<PingRow2>(
            "SELECT TOP 1 Lat, Lng, SpeedKmh FROM dbo.TripPings WHERE TripId = @tripId ORDER BY At DESC",
            new { tripId }, ct)).FirstOrDefault();

        if (ping is null || stops.Count == 0)
            return new BusPositionResponse(busId, 0, 0, ping?.Lat, ping?.Lng, null, null);

        int nearest = 0; double best = double.MaxValue;
        for (int i = 0; i < stops.Count; i++)
        {
            var dist = Haversine(ping.Lat, ping.Lng, stops[i].Lat, stops[i].Lng);
            if (dist < best) { best = dist; nearest = i; }
        }
        double progress = stops.Count > 1 ? Math.Round((double)nearest / (stops.Count - 1), 3) : 0;
        int? nextIndex = nearest + 1 < stops.Count ? nearest + 1 : null;
        string? next = nextIndex is int ni ? stops[ni].Name : null;

        int? etaMinutes = null;
        if (nextIndex is int idx && ping.SpeedKmh > 1.0) // ignore near-stationary/missing-speed noise
        {
            var distToNextMeters = Haversine(ping.Lat, ping.Lng, stops[idx].Lat, stops[idx].Lng);
            var etaHours = (distToNextMeters / 1000.0) / ping.SpeedKmh;
            etaMinutes = (int)Math.Round(etaHours * 60);
        }

        return new BusPositionResponse(busId, nearest, progress, ping.Lat, ping.Lng, next, etaMinutes);
    }

    private static double Haversine(double lat1, double lng1, double lat2, double lng2)
    {
        const double r = 6371000;
        double dLat = (lat2 - lat1) * Math.PI / 180, dLng = (lng2 - lng1) * Math.PI / 180;
        double a = Math.Sin(dLat/2)*Math.Sin(dLat/2) + Math.Cos(lat1*Math.PI/180)*Math.Cos(lat2*Math.PI/180)*Math.Sin(dLng/2)*Math.Sin(dLng/2);
        return r * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }

    internal static string Initials(string name)
    {
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return "";
        return parts.Length == 1 ? parts[0][..1].ToUpperInvariant()
            : (parts[0][..1] + parts[^1][..1]).ToUpperInvariant();
    }
}

public static class BusModule
{
    public static IServiceCollection AddBusModule(this IServiceCollection services)
    {
        services.AddScoped<BusRepository>();
        return services;
    }
}
