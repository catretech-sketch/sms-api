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
    Guid BusId, Guid? RouteId, string BusNo, string? RouteName, string? Driver, string? DriverPhone,
    int StopCount, int StudentsRiding, string Status,
    double? Lat, double? Lng, double? SpeedKmh, string? NextStopName, DateTime? LastPingAt,
    Guid? TeacherUserId = null, string? TeacherName = null);

public sealed record TransportRouteListItem(Guid Id, string Name, int Stops);
public sealed record RouteStopListItem(Guid Id, Guid RouteId, string Name, int Sequence, double Lat, double Lng);
public sealed record CreatedBusRow(
    Guid BusId, string BusNo, Guid? RouteId, string? RouteName, string? Driver, string? DriverPhone,
    int StopCount, int StudentsRiding, string Status, Guid? ConductorStaffId = null);
public sealed record UpdatedBusRow(
    Guid BusId, string BusNo, Guid? RouteId, string? RouteName, Guid? DriverStaffId,
    string? Driver, string? DriverPhone, int StopCount, int StudentsAssigned, Guid? ConductorStaffId = null);
public sealed record BusTripContext(Guid BusId, string BusNo, Guid? RouteId);

/// Admin bus list row with optional duty teacher.
public sealed record TransportBusResponse(
    Guid BusId, string BusNo, Guid? RouteId, string? RouteName, Guid? DriverStaffId,
    string? Driver, string? DriverPhone,
    int StopCount, int StudentsAssigned, Guid? TeacherUserId, string? TeacherName,
    Guid? ConductorStaffId = null);

public sealed record BusTeacherAssignmentResponse(
    Guid BusId, string BusNo, Guid? TeacherUserId, string? TeacherName);

/// Raw per-bus fleet row before status / next-stop derivation.
public sealed record FleetBusRow(
    Guid BusId, Guid? RouteId, string BusNo, string? RouteName, string? Driver, string? DriverPhone,
    int StopCount, Guid? TripId, double? Lat, double? Lng, double? SpeedKmh, DateTime? LastPingAt, int StudentsRiding);

public sealed class BusRepository(IDbConnectionFactory factory) : BaseRepository(factory)
{
    private sealed record BusRow(Guid Id, string BusNo, string? RouteName, string? Driver, string? DriverPhone);
    private sealed record RosterRow(Guid StudentId, string StudentName, Guid? StopId, string Status);
    private sealed record BusListRow(
        Guid BusId, string BusNo, Guid? RouteId, string? RouteName, Guid? DriverStaffId,
        string? Driver, string? DriverPhone,
        int StopCount, int StudentsAssigned, Guid? TeacherUserId, string? TeacherName);
    private sealed record RouteRow(Guid Id, string Name, int Stops);
    private sealed record RouteStopRow(Guid Id, Guid RouteId, string Name, int Seq, double Lat, double Lng);

    private const string StopCountSql =
        "CASE WHEN b.RouteId IS NOT NULL THEN (SELECT COUNT(*) FROM dbo.RouteStops rs WHERE rs.RouteId = b.RouteId) " +
        "ELSE (SELECT COUNT(*) FROM dbo.BusStops bs WHERE bs.BusId = b.Id) END";

    public async Task<BusResponse?> GetAssignedAsync(Guid teacherUserId, CancellationToken ct = default)
    {
        var bus = (await QueryInlineAsync<BusRow>(
            @"SELECT TOP 1 b.Id, b.BusNo, b.RouteName, b.Driver, b.DriverPhone
              FROM dbo.Buses b JOIN dbo.BusAssignments a ON a.BusId = b.Id
              WHERE a.TeacherUserId = @teacherUserId", new { teacherUserId }, ct)).FirstOrDefault();
        if (bus is null) return null;
        var stops = await QueryStopsForBusAsync(bus.Id, ct);
        return new BusResponse(bus.Id, bus.BusNo, bus.RouteName, bus.Driver, bus.DriverPhone, stops);
    }

    private async Task<IReadOnlyList<BusStopResponse>> QueryStopsForBusAsync(Guid busId, CancellationToken ct = default)
    {
        var routeId = (await QueryInlineAsync<Guid?>(
            "SELECT RouteId FROM dbo.Buses WHERE Id = @busId", new { busId }, ct)).FirstOrDefault();
        if (routeId is Guid rid)
        {
            return await QueryInlineAsync<BusStopResponse>(
                "SELECT Id, Name, CAST(NULL AS nvarchar(10)) AS Time, Seq, Lat, Lng FROM dbo.RouteStops WHERE RouteId = @routeId ORDER BY Seq",
                new { routeId = rid }, ct);
        }
        return await QueryInlineAsync<BusStopResponse>(
            "SELECT Id, Name, Time, Seq, Lat, Lng FROM dbo.BusStops WHERE BusId = @busId ORDER BY Seq",
            new { busId }, ct);
    }

    public async Task<Guid?> GetLiveTripIdForBusAsync(Guid busId, CancellationToken ct = default) =>
        await CurrentTripIdAsync(busId, ct);

    public async Task<BusTripContext?> GetBusTripContextAsync(Guid busId, CancellationToken ct = default) =>
        (await QueryInlineAsync<BusTripContext>(
            "SELECT Id AS BusId, BusNo, RouteId FROM dbo.Buses WHERE Id = @busId", new { busId }, ct)).FirstOrDefault();

    public async Task<CreatedBusRow?> CreateBusAsync(
        Guid tenantId, string busNo, string? routeName, Guid? routeId, string? driver, string? driverPhone,
        Guid? driverStaffId, Guid? conductorStaffId = null, CancellationToken ct = default) =>
        await QuerySingleProcAsync<CreatedBusRow>("dbo.Bus_Create",
            new
            {
                TenantId = tenantId, BusNo = busNo, RouteName = routeName, RouteId = routeId,
                Driver = driver, DriverPhone = driverPhone, DriverStaffId = driverStaffId,
                ConductorStaffId = conductorStaffId
            }, ct);

    public async Task<UpdatedBusRow?> UpdateBusAsync(
        Guid tenantId, Guid busId, string? busNo, Guid? routeId, Guid? driverStaffId, bool clearDriver,
        Guid? conductorStaffId = null, bool clearConductor = false,
        CancellationToken ct = default) =>
        await QuerySingleProcAsync<UpdatedBusRow>("dbo.Bus_Update",
            new
            {
                TenantId = tenantId, BusId = busId, BusNo = busNo, RouteId = routeId,
                DriverStaffId = driverStaffId, ClearDriver = clearDriver,
                ConductorStaffId = conductorStaffId, ClearConductor = clearConductor
            }, ct);

    public async Task<bool> StaffExistsAsync(Guid staffId, CancellationToken ct = default) =>
        (await QueryInlineAsync<int>(
            "SELECT COUNT(1) FROM dbo.Staff WHERE Id = @staffId", new { staffId }, ct)).First() > 0;

    public async Task<IReadOnlyList<TransportRouteListItem>> ListRoutesAsync(CancellationToken ct = default)
    {
        var rows = await QueryInlineAsync<RouteRow>(
            @"SELECT r.Id, r.Name,
                (SELECT COUNT(*) FROM dbo.RouteStops s WHERE s.RouteId = r.Id) AS Stops
              FROM dbo.TransportRoutes r ORDER BY r.Name", null, ct);
        return rows.Select(r => new TransportRouteListItem(r.Id, r.Name, r.Stops)).ToList();
    }

    public async Task<TransportRouteListItem?> CreateRouteAsync(Guid tenantId, string name, int stops, CancellationToken ct = default)
    {
        var row = await QuerySingleProcAsync<RouteRow>("dbo.TransportRoute_Create",
            new { TenantId = tenantId, Name = name, Stops = stops }, ct);
        return row is null ? null : new TransportRouteListItem(row.Id, row.Name, row.Stops);
    }

    public async Task<IReadOnlyList<RouteStopListItem>> ListRouteStopsAsync(Guid routeId, CancellationToken ct = default)
    {
        var rows = await QueryInlineAsync<RouteStopRow>(
            "SELECT Id, RouteId, Name, Seq, Lat, Lng FROM dbo.RouteStops WHERE RouteId = @routeId ORDER BY Seq",
            new { routeId }, ct);
        return rows.Select(s => new RouteStopListItem(s.Id, s.RouteId, s.Name, s.Seq, s.Lat, s.Lng)).ToList();
    }

    public async Task<bool> RouteStopExistsAsync(Guid stopId, CancellationToken ct = default) =>
        (await QueryInlineAsync<int>("SELECT COUNT(1) FROM dbo.RouteStops WHERE Id = @stopId", new { stopId }, ct)).First() > 0;

    public async Task<RouteStopListItem?> CreateRouteStopAsync(
        Guid tenantId, Guid routeId, string name, double lat, double lng, CancellationToken ct = default)
    {
        var seq = (await QueryInlineAsync<int>(
            "SELECT ISNULL(MAX(Seq), 0) + 1 FROM dbo.RouteStops WHERE RouteId = @routeId", new { routeId }, ct)).First();
        var id = Guid.NewGuid();
        await ExecuteInlineAsync(
            "INSERT INTO dbo.RouteStops (Id, TenantId, RouteId, Name, Seq, Lat, Lng) VALUES (@id, @tenantId, @routeId, @name, @seq, @lat, @lng)",
            new { id, tenantId, routeId, name, seq, lat, lng }, ct);
        return new RouteStopListItem(id, routeId, name, seq, lat, lng);
    }

    public async Task<RouteStopListItem?> UpdateRouteStopAsync(
        Guid stopId, string name, double lat, double lng, CancellationToken ct = default)
    {
        var row = (await QueryInlineAsync<RouteStopRow>(
            "SELECT Id, RouteId, Name, Seq, Lat, Lng FROM dbo.RouteStops WHERE Id = @stopId", new { stopId }, ct)).FirstOrDefault();
        if (row is null) return null;
        await ExecuteInlineAsync(
            "UPDATE dbo.RouteStops SET Name = @name, Lat = @lat, Lng = @lng WHERE Id = @stopId",
            new { stopId, name, lat, lng }, ct);
        return new RouteStopListItem(row.Id, row.RouteId, name, row.Seq, lat, lng);
    }

    public async Task<bool> DeleteRouteStopAsync(Guid routeId, Guid stopId, CancellationToken ct = default)
    {
        var n = await ExecuteInlineAsync(
            "DELETE FROM dbo.RouteStops WHERE Id = @stopId AND RouteId = @routeId",
            new { stopId, routeId }, ct);
        return n > 0;
    }

    public async Task ReorderRouteStopsAsync(Guid routeId, IReadOnlyList<Guid> stopIds, CancellationToken ct = default)
    {
        for (var i = 0; i < stopIds.Count; i++)
            await ExecuteInlineAsync(
                "UPDATE dbo.RouteStops SET Seq = @seq WHERE Id = @id AND RouteId = @routeId",
                new { seq = i + 1, id = stopIds[i], routeId }, ct);
    }
    private async Task<Guid?> CurrentTripIdAsync(Guid busId, CancellationToken ct) =>
        (await QueryInlineAsync<Guid>(
            @"SELECT TOP 1 t.Id FROM dbo.Trips t
              WHERE t.BusId = @busId AND t.Status = 'live' ORDER BY t.StartedAt DESC",
            new { busId }, ct)).Cast<Guid?>().FirstOrDefault();

    public async Task<IReadOnlyList<BusRosterEntry>> GetRosterAsync(Guid busId, CancellationToken ct = default)
    {
        var tripId = await CurrentTripIdAsync(busId, ct);
        IReadOnlyList<RosterRow> rows;
        if (tripId is null)
        {
            rows = await QueryInlineAsync<RosterRow>(
                @"SELECT sba.StudentId, s.Name AS StudentName, sba.StopId, 'pending' AS Status
                  FROM dbo.StudentBusAssignments sba
                  JOIN dbo.Students s ON s.Id = sba.StudentId
                  WHERE sba.BusId = @busId
                    AND NOT EXISTS (
                      SELECT 1 FROM dbo.StudentTransportOptOut o
                      WHERE o.StudentId = sba.StudentId AND o.TenantId = sba.TenantId)
                  ORDER BY s.Name", new { busId }, ct);
        }
        else
        {
            rows = await QueryInlineAsync<RosterRow>(
                @"SELECT sba.StudentId, s.Name AS StudentName, sba.StopId,
                         COALESCE(bo.State, 'pending') AS Status
                  FROM dbo.StudentBusAssignments sba
                  JOIN dbo.Students s ON s.Id = sba.StudentId
                  LEFT JOIN dbo.Boardings bo ON bo.TripId = @tripId AND bo.StudentId = sba.StudentId
                  WHERE sba.BusId = @busId
                    AND NOT EXISTS (
                      SELECT 1 FROM dbo.StudentTransportOptOut o
                      WHERE o.StudentId = sba.StudentId AND o.TenantId = sba.TenantId)
                  ORDER BY s.Name", new { busId, tripId }, ct);
        }

        return rows.Select(r => new BusRosterEntry(r.StudentId, r.StudentName, Initials(r.StudentName), r.StopId, r.Status)).ToList();
    }

    public async Task<IReadOnlyList<TransportBusResponse>> ListBusesAsync(CancellationToken ct = default)
    {
        var rows = await QueryInlineAsync<BusListRow>(
            $@"SELECT b.Id AS BusId, b.BusNo, b.RouteId, b.RouteName, b.DriverStaffId, b.Driver, b.DriverPhone,
                {StopCountSql} AS StopCount,
                (SELECT COUNT(*) FROM dbo.StudentBusAssignments sba WHERE sba.BusId = b.Id) AS StudentsAssigned,
                a.TeacherUserId, u.Name AS TeacherName
              FROM dbo.Buses b
              LEFT JOIN dbo.BusAssignments a ON a.BusId = b.Id
              LEFT JOIN dbo.Users u ON u.Id = a.TeacherUserId
              ORDER BY b.BusNo", null, ct);
        return rows.Select(r => new TransportBusResponse(
            r.BusId, r.BusNo, r.RouteId, r.RouteName, r.DriverStaffId, r.Driver, r.DriverPhone,
            r.StopCount, r.StudentsAssigned, r.TeacherUserId, r.TeacherName)).ToList();
    }

    public async Task<BusTeacherAssignmentResponse?> GetTeacherAssignmentAsync(Guid busId, CancellationToken ct = default)
    {
        var row = (await QueryInlineAsync<BusListRow>(
            $@"SELECT b.Id AS BusId, b.BusNo, b.RouteId, b.RouteName, b.DriverStaffId, b.Driver, b.DriverPhone,
                {StopCountSql} AS StopCount,
                (SELECT COUNT(*) FROM dbo.StudentBusAssignments sba WHERE sba.BusId = b.Id) AS StudentsAssigned,
                a.TeacherUserId, u.Name AS TeacherName
              FROM dbo.Buses b
              LEFT JOIN dbo.BusAssignments a ON a.BusId = b.Id
              LEFT JOIN dbo.Users u ON u.Id = a.TeacherUserId
              WHERE b.Id = @busId", new { busId }, ct)).FirstOrDefault();
        return row is null ? null : new BusTeacherAssignmentResponse(row.BusId, row.BusNo, row.TeacherUserId, row.TeacherName);
    }

    public Task AssignTeacherAsync(Guid tenantId, Guid busId, Guid teacherUserId, CancellationToken ct = default) =>
        ExecuteProcAsync("dbo.BusAssignment_Assign", new { TenantId = tenantId, BusId = busId, TeacherUserId = teacherUserId }, ct);

    public Task UnassignTeacherAsync(Guid tenantId, Guid busId, CancellationToken ct = default) =>
        ExecuteProcAsync("dbo.BusAssignment_Unassign", new { TenantId = tenantId, BusId = busId }, ct);

    public async Task<bool> BusExistsAsync(Guid busId, CancellationToken ct = default) =>
        (await QueryInlineAsync<int>("SELECT COUNT(1) FROM dbo.Buses WHERE Id = @busId", new { busId }, ct)).First() > 0;

    public async Task<bool> RouteExistsAsync(Guid routeId, CancellationToken ct = default) =>
        (await QueryInlineAsync<int>("SELECT COUNT(1) FROM dbo.TransportRoutes WHERE Id = @routeId", new { routeId }, ct)).First() > 0;

    /// Every bus with its current live trip (matched by BusNo), latest GPS ping and boarded count.
    public Task<IReadOnlyList<FleetBusRow>> FleetAsync(CancellationToken ct = default) =>
        QueryInlineAsync<FleetBusRow>(
            $@"SELECT b.Id AS BusId, b.RouteId, b.BusNo, b.RouteName, b.Driver, b.DriverPhone,
                {StopCountSql} AS StopCount,
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
                (SELECT COUNT(*) FROM dbo.TransportRoutes) AS Routes,
                (SELECT COUNT(DISTINCT sba.StudentId) FROM dbo.StudentBusAssignments sba) AS Students,
                (SELECT COUNT(*) FROM dbo.RouteStops) +
                (SELECT COUNT(*) FROM dbo.BusStops bs
                   WHERE NOT EXISTS (SELECT 1 FROM dbo.Buses bx WHERE bx.Id = bs.BusId AND bx.RouteId IS NOT NULL)) AS Stops",
            null, ct)).First();

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
            @"SELECT Name, Seq, Lat, Lng FROM (
                SELECT rs.Name, rs.Seq, rs.Lat, rs.Lng
                FROM dbo.Buses b INNER JOIN dbo.RouteStops rs ON rs.RouteId = b.RouteId
                WHERE b.Id = @busId AND b.RouteId IS NOT NULL
                UNION ALL
                SELECT bs.Name, bs.Seq, bs.Lat, bs.Lng
                FROM dbo.BusStops bs INNER JOIN dbo.Buses b ON b.Id = bs.BusId
                WHERE bs.BusId = @busId AND b.RouteId IS NULL
              ) s ORDER BY Seq", new { busId }, ct);
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
