using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Sms.Shared.Kernel.Authz;
using Sms.Shared.Kernel.Data;
using Sms.Shared.Kernel.Http;
using Sms.Shared.Kernel.Results;
using Sms.Shared.Kernel.Tenancy;

namespace Sms.Modules.Transport;

public sealed record BusStopResponse(Guid Id, string Name, string? Time, int Seq, double Lat, double Lng);
public sealed record BusResponse(
    Guid Id, string BusNo, string? RouteName, string? Driver, string? DriverPhone, IReadOnlyList<BusStopResponse> Stops);
public sealed record BusRosterEntry(Guid StudentId, string StudentName, string Initials, Guid? StopId, string Status);

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
            @"SELECT TOP 1 t.Id FROM dbo.Trips t JOIN dbo.Buses b ON b.BusNo = t.BusNo
              WHERE b.Id = @busId AND t.Status = 'live' ORDER BY t.StartedAt DESC",
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

    internal static IResult Forbidden(string message) =>
        Results.Json(ErrorEnvelope.From(new Error("forbidden", message)), statusCode: 403);

    /// Phase 4: teacher bus-duty view under /v1/bus*. Tenant-scoped; assigned is user-scoped.
    public static IEndpointRouteBuilder MapBusModule(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/v1/bus").RequireAuthorization(AuthorizationPolicies.TeacherApp);

        g.MapGet("/assigned", async (BusRepository repo, ITenantContext tenant) =>
        {
            if (tenant.UserId is not { } uid) return Forbidden("no user context");
            var bus = await repo.GetAssignedAsync(uid);
            return bus is null
                ? Results.Json(ErrorEnvelope.From(new Error("not_found", "no assigned bus")), statusCode: 404)
                : Results.Ok(new DataEnvelope<BusResponse>(bus));
        });

        g.MapGet("/{busId:guid}/roster", async (Guid busId, BusRepository repo) =>
            Results.Ok(new DataEnvelope<IReadOnlyList<BusRosterEntry>>(await repo.GetRosterAsync(busId))));

        return app;
    }
}
