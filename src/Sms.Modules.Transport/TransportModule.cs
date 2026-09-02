using System.Data;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Sms.Shared.Kernel.Data;

namespace Sms.Modules.Transport;

// ActiveBroadcaster is deliberately NOT a primary-constructor parameter: Dapper's record
// materialization requires an exact positional match between the constructor and the
// selected columns, so a computed-only field (never selected from the DB) must live outside
// the constructor as an init-only property instead, set afterwards via `with`.
public sealed record TripResponse(
    Guid Id, Guid TenantId, Guid? RouteId, string? BusNo, Guid? DriverId, Guid? ConductorId,
    string Direction, string Status, DateTime? StartedAt, DateTime? EndedAt,
    DateTime? DriverLastPingAt = null, DateTime? ConductorLastPingAt = null)
{
    public string? ActiveBroadcaster { get; init; }
}
public sealed record StartTripRequest(Guid? RouteId, string? BusNo, string Direction);
public sealed record PingItem(double Lat, double Lng, double SpeedKmh, double Heading, DateTime At);
public sealed record BulkPingRequest(IReadOnlyList<PingItem> Pings);
public sealed record TripSummaryResponse(Guid TripId, int DurationMin, double DistanceKm, int StopsCovered, int BoardedCount);
public sealed record BoardingResponse(Guid TripId, Guid StudentId, Guid? StopId, string State, DateTime At);
public sealed record BoardingRequest(Guid StudentId, Guid? StopId, string State, DateTime At);
public sealed record StaffStopResponse(Guid Id, string Name, double Lat, double Lng, int Seq, int? EtaMin);
public sealed record StaffRouteResponse(Guid Id, string Name, string BusNo, IReadOnlyList<StaffStopResponse> Stops);
public sealed record StaffTripAssignmentResponse(StaffRouteResponse Route, string BusNo, string? ConductorName);
public sealed record StaffRosterStudentResponse(Guid Id, string Name, Guid? StopId, string? PhotoUrl);
public sealed record StaffBusRouteSummaryResponse(string BusNo, string RouteName);

public sealed class TripRepository(IDbConnectionFactory factory) : BaseRepository(factory)
{
    private const string TripCols =
        "Id, TenantId, RouteId, BusNo, DriverId, ConductorId, Direction, Status, StartedAt, EndedAt, " +
        "DriverLastPingAt, ConductorLastPingAt";
    private sealed record PingRow(double Lat, double Lng);

    public Task<TripResponse?> StartAsync(Guid tenantId, Guid driverId, StartTripRequest r, CancellationToken ct = default) =>
        QuerySingleProcAsync<TripResponse>("dbo.Trip_Start",
            new { TenantId = tenantId, r.RouteId, r.BusNo, DriverId = driverId, r.Direction }, ct);

    public async Task<TripResponse?> GetCurrentAsync(Guid userId, CancellationToken ct = default) =>
        (await QueryInlineAsync<TripResponse>(
            $"SELECT TOP 1 {TripCols} FROM dbo.Trips WHERE (DriverId = @userId OR ConductorId = @userId) AND Status = 'live' ORDER BY StartedAt DESC",
            new { userId }, ct)).FirstOrDefault();

    private sealed record TripParticipantsRow(Guid? DriverId, Guid? ConductorId);

    /// Returns "driver", "conductor", or null if the caller is neither — the trip's driver or
    /// its assigned conductor may operate it, RLS already scopes the row to the caller's tenant.
    /// Guards driver-app mutations against acting on a peer's trip within the same school.
    public async Task<string?> GetParticipantRoleAsync(Guid tripId, Guid userId, CancellationToken ct = default)
    {
        var row = (await QueryInlineAsync<TripParticipantsRow>(
            "SELECT DriverId, ConductorId FROM dbo.Trips WHERE Id = @tripId",
            new { tripId }, ct)).FirstOrDefault();
        if (row is null) return null;
        if (row.DriverId == userId) return "driver";
        if (row.ConductorId == userId) return "conductor";
        return null;
    }

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

    public Task MarkPingAsync(Guid tripId, string role, CancellationToken ct = default)
    {
        var column = role == "driver" ? "DriverLastPingAt" : "ConductorLastPingAt";
        return ExecuteInlineAsync(
            $"UPDATE dbo.Trips SET {column} = SYSUTCDATETIME() WHERE Id = @tripId", new { tripId }, ct);
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

    private sealed record AssignedBusRow(string BusNo, Guid? RouteId, string? ConductorName);
    private sealed record RouteRow(Guid Id, string Name);

    /// Resolved by the driver's own identity (Staff.UserId -> Buses.DriverStaffId), never by a
    /// client-supplied id, so the Trip screen's first load needs nothing but the caller's JWT.
    public async Task<StaffTripAssignmentResponse?> GetAssignmentAsync(Guid driverUserId, CancellationToken ct = default)
    {
        var bus = (await QueryInlineAsync<AssignedBusRow>(
            @"SELECT b.BusNo, b.RouteId, cs.Name AS ConductorName
              FROM dbo.Buses b
              JOIN dbo.Staff s ON s.Id = b.DriverStaffId
              LEFT JOIN dbo.Staff cs ON cs.Id = b.ConductorStaffId
              WHERE s.UserId = @driverUserId", new { driverUserId }, ct)).FirstOrDefault();
        if (bus?.RouteId is not { } routeId) return null;

        var route = (await QueryInlineAsync<RouteRow>(
            "SELECT Id, Name FROM dbo.TransportRoutes WHERE Id = @routeId", new { routeId }, ct)).FirstOrDefault();
        if (route is null) return null;

        var stops = await QueryInlineAsync<StaffStopResponse>(
            "SELECT Id, Name, Lat, Lng, Seq, CAST(NULL AS int) AS EtaMin FROM dbo.RouteStops WHERE RouteId = @routeId ORDER BY Seq",
            new { routeId }, ct);

        return new StaffTripAssignmentResponse(
            new StaffRouteResponse(route.Id, route.Name, bus.BusNo, stops), bus.BusNo, bus.ConductorName);
    }

    /// Lightweight bus+route lookup for the staff dashboard's role card — driver/conductor
    /// resolved from their own identity, matching whichever of DriverStaffId/ConductorStaffId
    /// applies. Unlike GetAssignmentAsync, doesn't need stops or the peer's name.
    public async Task<StaffBusRouteSummaryResponse?> GetDriverBusRouteAsync(Guid driverUserId, CancellationToken ct = default) =>
        (await QueryInlineAsync<StaffBusRouteSummaryResponse>(
            @"SELECT b.BusNo, r.Name AS RouteName
              FROM dbo.Buses b
              JOIN dbo.Staff s ON s.Id = b.DriverStaffId
              JOIN dbo.TransportRoutes r ON r.Id = b.RouteId
              WHERE s.UserId = @driverUserId", new { driverUserId }, ct)).FirstOrDefault();

    public async Task<StaffBusRouteSummaryResponse?> GetConductorBusRouteAsync(Guid conductorUserId, CancellationToken ct = default) =>
        (await QueryInlineAsync<StaffBusRouteSummaryResponse>(
            @"SELECT b.BusNo, r.Name AS RouteName
              FROM dbo.Buses b
              JOIN dbo.Staff s ON s.Id = b.ConductorStaffId
              JOIN dbo.TransportRoutes r ON r.Id = b.RouteId
              WHERE s.UserId = @conductorUserId", new { conductorUserId }, ct)).FirstOrDefault();

    public async Task<IReadOnlyList<StaffRosterStudentResponse>> GetRosterAsync(Guid tripId, CancellationToken ct = default)
    {
        var busId = (await QueryInlineAsync<Guid?>(
            "SELECT BusId FROM dbo.Trips WHERE Id = @tripId", new { tripId }, ct)).FirstOrDefault();
        if (busId is null) return [];

        return await QueryInlineAsync<StaffRosterStudentResponse>(
            @"SELECT s.Id, s.Name, sba.StopId, s.PhotoUrl
              FROM dbo.StudentBusAssignments sba
              JOIN dbo.Students s ON s.Id = sba.StudentId
              WHERE sba.BusId = @busId
              ORDER BY s.Name", new { busId }, ct);
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
