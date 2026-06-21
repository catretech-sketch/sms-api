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

public sealed class BusRepository(IDbConnectionFactory factory) : BaseRepository(factory)
{
    private sealed record BusRow(Guid Id, string BusNo, string? RouteName, string? Driver, string? DriverPhone);

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

        return app;
    }
}
