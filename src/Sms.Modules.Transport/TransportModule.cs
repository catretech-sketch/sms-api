using System.Data;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Sms.Shared.Kernel.Data;

namespace Sms.Modules.Transport;

public sealed record TripResponse(
    Guid Id, Guid TenantId, Guid? RouteId, string? BusNo, Guid? DriverId, Guid? ConductorId,
    string Direction, string Status, DateTime? StartedAt, DateTime? EndedAt);
public sealed record StartTripRequest(Guid? RouteId, string? BusNo, string Direction);
public sealed record PingItem(double Lat, double Lng, double SpeedKmh, double Heading, DateTime At);
public sealed record BulkPingRequest(IReadOnlyList<PingItem> Pings);
public sealed record TripSummaryResponse(Guid TripId, int DurationMin, double DistanceKm, int StopsCovered, int BoardedCount);
public sealed record BoardingResponse(Guid TripId, Guid StudentId, Guid? StopId, string State, DateTime At);
public sealed record BoardingRequest(Guid StudentId, Guid? StopId, string State, DateTime At);

public sealed class TripRepository(IDbConnectionFactory factory) : BaseRepository(factory)
{
    private const string TripCols =
        "Id, TenantId, RouteId, BusNo, DriverId, ConductorId, Direction, Status, StartedAt, EndedAt";
    private sealed record PingRow(double Lat, double Lng);

    public Task<TripResponse?> StartAsync(Guid tenantId, Guid driverId, StartTripRequest r, CancellationToken ct = default) =>
        QuerySingleProcAsync<TripResponse>("dbo.Trip_Start",
            new { TenantId = tenantId, r.RouteId, r.BusNo, DriverId = driverId, r.Direction }, ct);

    public async Task<TripResponse?> GetCurrentAsync(Guid driverId, CancellationToken ct = default) =>
        (await QueryInlineAsync<TripResponse>(
            $"SELECT TOP 1 {TripCols} FROM dbo.Trips WHERE DriverId = @driverId AND Status = 'live' ORDER BY StartedAt DESC",
            new { driverId }, ct)).FirstOrDefault();

    public Task IngestPingsAsync(Guid tenantId, Guid tripId, IReadOnlyList<PingItem> pings, CancellationToken ct = default)
    {
        var table = new DataTable();
        table.Columns.Add("Lat", typeof(double));
        table.Columns.Add("Lng", typeof(double));
        table.Columns.Add("SpeedKmh", typeof(double));
        table.Columns.Add("Heading", typeof(double));
        table.Columns.Add("At", typeof(DateTime));
        foreach (var p in pings) table.Rows.Add(p.Lat, p.Lng, p.SpeedKmh, p.Heading, p.At);

        var args = new DynamicParameters();
        args.Add("@TenantId", tenantId);
        args.Add("@TripId", tripId);
        args.Add("@Rows", table.AsTableValuedParameter("dbo.TripPingTvp"));
        return ExecuteProcAsync("dbo.TripPing_BulkInsert", args, ct);
    }

    public async Task<TripSummaryResponse> EndAsync(Guid tripId, CancellationToken ct = default)
    {
        var trip = await QuerySingleProcAsync<TripResponse>("dbo.Trip_End", new { Id = tripId }, ct);
        var pings = await QueryInlineAsync<PingRow>(
            "SELECT Lat, Lng FROM dbo.TripPings WHERE TripId = @tripId ORDER BY At", new { tripId }, ct);

        double metres = 0;
        for (var i = 1; i < pings.Count; i++)
            metres += Haversine(pings[i - 1].Lat, pings[i - 1].Lng, pings[i].Lat, pings[i].Lng);

        var durationMin = trip is { StartedAt: { } s, EndedAt: { } e } ? (int)(e - s).TotalMinutes : 0;
        var boarded = (await QueryInlineAsync<int>(
            "SELECT COUNT(*) FROM dbo.Boardings WHERE TripId = @tripId AND State = 'boarded'", new { tripId }, ct)).First();
        var stops = (await QueryInlineAsync<int>(
            "SELECT COUNT(DISTINCT StopId) FROM dbo.Boardings WHERE TripId = @tripId AND StopId IS NOT NULL",
            new { tripId }, ct)).First();

        return new TripSummaryResponse(tripId, durationMin, Math.Round(metres / 1000, 2), stops, boarded);
    }

    public Task<IReadOnlyList<BoardingResponse>> ListBoardingAsync(Guid tripId, CancellationToken ct = default) =>
        QueryInlineAsync<BoardingResponse>(
            "SELECT TripId, StudentId, StopId, State, At FROM dbo.Boardings WHERE TripId = @tripId ORDER BY At",
            new { tripId }, ct);

    public Task UpsertBoardingAsync(Guid tenantId, Guid tripId, BoardingRequest r, CancellationToken ct = default) =>
        ExecuteProcAsync("dbo.Boarding_Upsert",
            new { TenantId = tenantId, TripId = tripId, r.StudentId, r.StopId, r.State, r.At }, ct);

    private static double Haversine(double lat1, double lng1, double lat2, double lng2)
    {
        const double radius = 6371000;
        double dLat = (lat2 - lat1) * Math.PI / 180;
        double dLng = (lng2 - lng1) * Math.PI / 180;
        double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                   Math.Cos(lat1 * Math.PI / 180) * Math.Cos(lat2 * Math.PI / 180) *
                   Math.Sin(dLng / 2) * Math.Sin(dLng / 2);
        return radius * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }
}

public static class TransportModule
{
    public static IServiceCollection AddTransportModule(this IServiceCollection services)
    {
        services.AddScoped<TripRepository>();
        return services;
    }
}
