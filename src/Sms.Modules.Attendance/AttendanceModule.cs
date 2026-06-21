using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Sms.Shared.Kernel.Data;
using Sms.Shared.Kernel.Http;
using Sms.Shared.Kernel.Results;
using Sms.Shared.Kernel.Tenancy;
using Sms.Shared.Kernel.Time;

namespace Sms.Modules.Attendance;

public sealed record SchoolLocationResponse(double Lat, double Lng, int RadiusMeters, string? Name);
public sealed record UpsertSchoolLocationRequest(double Lat, double Lng, int RadiusMeters, string? Name);
public sealed record CheckEventResponse(
    string Kind, DateTime At, double Lat, double Lng, double AccuracyMeters, double DistanceMeters, bool Verified);
public sealed record PunchRequest(string Kind, DateTime At, double Lat, double Lng, double AccuracyMeters);
public sealed record TeacherAttendanceDayResponse(DateTime Date, CheckEventResponse? CheckIn, CheckEventResponse? CheckOut);
public sealed record TeacherAttendanceSummaryResponse(int DaysPresent, int DaysFlagged, double TotalHours);

public sealed class CheckInRepository(IDbConnectionFactory factory) : BaseRepository(factory)
{
    private const double AccuracyCapMeters = 30;
    private sealed record CheckInRow(string Kind, DateTime At, double Lat, double Lng, double AccuracyMeters, double DistanceMeters, bool Verified);

    public async Task<SchoolLocationResponse?> GetSchoolLocationAsync(Guid tenantId, CancellationToken ct = default) =>
        (await QueryInlineAsync<SchoolLocationResponse>(
            "SELECT Lat, Lng, RadiusMeters, Name FROM dbo.SchoolLocations WHERE TenantId = @tenantId",
            new { tenantId }, ct)).FirstOrDefault();

    public async Task<SchoolLocationResponse?> UpsertSchoolLocationAsync(
        Guid tenantId, UpsertSchoolLocationRequest r, CancellationToken ct = default) =>
        (await QueryProcAsync<SchoolLocationResponse>("dbo.SchoolLocation_Upsert",
            new { TenantId = tenantId, r.Lat, r.Lng, r.RadiusMeters, r.Name }, ct)).FirstOrDefault();

    /// Server-authoritative geofence verify: distance is computed here (haversine) from the stored
    /// school location, NOT trusted from the client. verified = distance <= radius + min(accuracy, cap).
    public async Task<CheckEventResponse> PunchAsync(Guid tenantId, Guid userId, PunchRequest r, CancellationToken ct = default)
    {
        var loc = await GetSchoolLocationAsync(tenantId, ct);
        double distance = 0;
        bool verified = false;
        if (loc is not null)
        {
            distance = Haversine(loc.Lat, loc.Lng, r.Lat, r.Lng);
            verified = distance <= loc.RadiusMeters + Math.Min(r.AccuracyMeters, AccuracyCapMeters);
        }

        await ExecuteProcAsync("dbo.CheckIn_Insert", new
        {
            TenantId = tenantId, UserId = userId, r.Kind, r.At, r.Lat, r.Lng, r.AccuracyMeters,
            DistanceMeters = distance, Verified = verified
        }, ct);

        return new CheckEventResponse(r.Kind, r.At, r.Lat, r.Lng, r.AccuracyMeters, Math.Round(distance, 1), verified);
    }

    public async Task<TeacherAttendanceDayResponse> GetTodayAsync(Guid userId, DateTime day, CancellationToken ct = default)
    {
        var rows = await QueryInlineAsync<CheckInRow>(
            "SELECT Kind, At, Lat, Lng, AccuracyMeters, DistanceMeters, Verified FROM dbo.CheckIns " +
            "WHERE UserId = @userId AND CAST(At AS date) = @day ORDER BY At",
            new { userId, day = day.Date }, ct);
        var ci = rows.Where(x => x.Kind == "in").Select(ToEvent).LastOrDefault();
        var co = rows.Where(x => x.Kind == "out").Select(ToEvent).LastOrDefault();
        return new TeacherAttendanceDayResponse(day.Date, ci, co);
    }

    public async Task<IReadOnlyList<TeacherAttendanceDayResponse>> GetHistoryAsync(
        Guid userId, int limit, CancellationToken ct = default)
    {
        var rows = await QueryInlineAsync<CheckInRow>(
            "SELECT Kind, At, Lat, Lng, AccuracyMeters, DistanceMeters, Verified FROM dbo.CheckIns " +
            "WHERE UserId = @userId ORDER BY At DESC", new { userId }, ct);

        return rows.GroupBy(r => r.At.Date)
            .OrderByDescending(g => g.Key)
            .Take(limit)
            .Select(g => new TeacherAttendanceDayResponse(
                g.Key,
                g.Where(x => x.Kind == "in").OrderBy(x => x.At).Select(ToEvent).LastOrDefault(),
                g.Where(x => x.Kind == "out").OrderBy(x => x.At).Select(ToEvent).LastOrDefault()))
            .ToList();
    }

    public async Task<TeacherAttendanceSummaryResponse> GetSummaryAsync(
        Guid userId, int year, int month, CancellationToken ct = default)
    {
        var rows = await QueryInlineAsync<CheckInRow>(
            "SELECT Kind, At, Lat, Lng, AccuracyMeters, DistanceMeters, Verified FROM dbo.CheckIns " +
            "WHERE UserId = @userId AND YEAR(At) = @year AND MONTH(At) = @month", new { userId, year, month }, ct);

        var byDay = rows.GroupBy(r => r.At.Date).ToList();
        int daysPresent = byDay.Count(g => g.Any(x => x.Kind == "in"));
        int daysFlagged = byDay.Count(g => g.Any(x => !x.Verified));
        double totalHours = byDay.Sum(g =>
        {
            var firstIn = g.Where(x => x.Kind == "in").OrderBy(x => x.At).Select(x => (DateTime?)x.At).FirstOrDefault();
            var lastOut = g.Where(x => x.Kind == "out").OrderBy(x => x.At).Select(x => (DateTime?)x.At).LastOrDefault();
            return firstIn is { } i && lastOut is { } o && o > i ? (o - i).TotalHours : 0;
        });
        return new TeacherAttendanceSummaryResponse(daysPresent, daysFlagged, Math.Round(totalHours, 2));
    }

    private static CheckEventResponse ToEvent(CheckInRow x) =>
        new(x.Kind, x.At, x.Lat, x.Lng, x.AccuracyMeters, x.DistanceMeters, x.Verified);

    private static double Haversine(double lat1, double lng1, double lat2, double lng2)
    {
        const double r = 6371000; // earth radius, metres
        double dLat = (lat2 - lat1) * Math.PI / 180;
        double dLng = (lng2 - lng1) * Math.PI / 180;
        double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                   Math.Cos(lat1 * Math.PI / 180) * Math.Cos(lat2 * Math.PI / 180) *
                   Math.Sin(dLng / 2) * Math.Sin(dLng / 2);
        return r * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }
}

public static class AttendanceModule
{
    public static IServiceCollection AddAttendanceModule(this IServiceCollection services)
    {
        services.AddScoped<CheckInRepository>();
        return services;
    }

    private static IResult Forbidden(string message) =>
        Results.Json(ErrorEnvelope.From(new Error("forbidden", message)), statusCode: 403);

    /// Phase 3: teacher geofenced self check-in under /v1/me/attendance/*. Tenant + user scoped.
    public static IEndpointRouteBuilder MapAttendanceModule(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/v1/me/attendance").RequireAuthorization();

        g.MapGet("/school-location", async (CheckInRepository repo, ITenantContext tenant) =>
        {
            if (tenant.TenantId is not { } tid) return Forbidden("no tenant context");
            var loc = await repo.GetSchoolLocationAsync(tid) ?? new SchoolLocationResponse(0, 0, 50, null);
            return Results.Ok(new DataEnvelope<SchoolLocationResponse>(loc));
        });

        g.MapPut("/school-location", async (UpsertSchoolLocationRequest req, CheckInRepository repo, ITenantContext tenant) =>
        {
            if (tenant.TenantId is not { } tid) return Forbidden("no tenant context");
            return Results.Ok(new DataEnvelope<SchoolLocationResponse>((await repo.UpsertSchoolLocationAsync(tid, req))!));
        });

        g.MapPost("/punch", async (PunchRequest req, CheckInRepository repo, ITenantContext tenant) =>
        {
            if (tenant.TenantId is not { } tid || tenant.UserId is not { } uid) return Forbidden("no tenant/user context");
            await repo.PunchAsync(tid, uid, req);
            var day = await repo.GetTodayAsync(uid, req.At);
            return Results.Json(new DataEnvelope<TeacherAttendanceDayResponse>(day), statusCode: 201);
        });

        g.MapGet("/today", async (CheckInRepository repo, ITenantContext tenant) =>
        {
            if (tenant.UserId is not { } uid) return Forbidden("no user context");
            return Results.Ok(new DataEnvelope<TeacherAttendanceDayResponse>(
                await repo.GetTodayAsync(uid, DateTime.UtcNow)));
        });

        g.MapGet("/history", async (CheckInRepository repo, ITenantContext tenant, [FromQuery] int? limit) =>
        {
            if (tenant.UserId is not { } uid) return Forbidden("no user context");
            return Results.Ok(new DataEnvelope<IReadOnlyList<TeacherAttendanceDayResponse>>(
                await repo.GetHistoryAsync(uid, limit is > 0 and <= 366 ? limit.Value : 30)));
        });

        g.MapGet("/summary", async (CheckInRepository repo, ITenantContext tenant, IClock clock, [FromQuery] string? month) =>
        {
            if (tenant.UserId is not { } uid) return Forbidden("no user context");
            var now = clock.UtcNow;
            int year = now.Year, m = now.Month;
            if (month is not null)
            {
                if (!System.Text.RegularExpressions.Regex.IsMatch(month, @"^\d{4}-\d{2}$"))
                    return Results.Json(ErrorEnvelope.From(new Error("invalid_month", "month must be YYYY-MM")), statusCode: 422);
                year = int.Parse(month[..4]); m = int.Parse(month[5..]);
            }
            return Results.Ok(new DataEnvelope<TeacherAttendanceSummaryResponse>(
                await repo.GetSummaryAsync(uid, year, m)));
        });

        return app;
    }
}
