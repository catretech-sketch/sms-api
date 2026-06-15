using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Sms.Modules.Tenancy.Contracts;
using Sms.Modules.Tenancy.Data;
using Sms.Shared.Kernel.Http;
using Sms.Shared.Kernel.Results;

namespace Sms.Modules.Tenancy;

public static class ModuleEndpoints
{
    /// Registers the Catre (platform super-admin) repositories.
    public static IServiceCollection AddTenancyModule(this IServiceCollection services)
    {
        services.AddScoped<ClientRepository>();
        services.AddScoped<PlanRepository>();
        return services;
    }

    private static IResult NotFound() =>
        Results.Json(ErrorEnvelope.From(new Error("not_found", "resource not found")), statusCode: 404);

    /// Catre Phase 1: /v1/clients + /v1/plans. Platform-scoped (Catre staff bypass tenant RLS).
    public static IEndpointRouteBuilder MapTenancyModule(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/v1").RequireAuthorization("platform");

        g.MapGet("/tenancy/_ping", () => Results.Ok(new { module = "tenancy", status = "live" }));

        // ---- Clients ----
        g.MapGet("/clients", async (ClientRepository repo, string? status, string? tier, string? q) =>
        {
            var rows = await repo.ListAsync(status, tier, q);
            return Results.Ok(new CursorPage<ClientResponse>(rows.Select(r => r.ToResponse()).ToList(), null));
        });

        g.MapGet("/clients/{id:guid}", async (Guid id, ClientRepository repo) =>
        {
            var row = await repo.GetAsync(id);
            return row is null ? NotFound() : Results.Ok(new DataEnvelope<ClientResponse>(row.ToResponse()));
        });

        g.MapPost("/clients", async (CreateClientRequest req, ClientRepository repo) =>
        {
            var row = await repo.CreateAsync(req);
            return Results.Json(new DataEnvelope<ClientResponse>(row!.ToResponse()), statusCode: 201);
        });

        g.MapPost("/clients/{id:guid}/status", async (Guid id, SetStatusRequest req, ClientRepository repo) =>
        {
            var row = await repo.SetStatusAsync(id, req.Status);
            return row is null ? NotFound() : Results.Ok(new DataEnvelope<ClientResponse>(row.ToResponse()));
        });

        g.MapPost("/clients/{id:guid}/change-plan", async (Guid id, ChangePlanRequest req, ClientRepository repo) =>
        {
            var row = await repo.ChangePlanAsync(id, req.PlanId);
            return row is null ? NotFound() : Results.Ok(new DataEnvelope<ClientResponse>(row.ToResponse()));
        });

        // ---- Plans ----
        g.MapGet("/plans", async (PlanRepository repo, string? visibility, string? audience) =>
        {
            var rows = await repo.ListAsync(visibility, audience);
            return Results.Ok(new DataEnvelope<IReadOnlyList<PlanResponse>>(rows.Select(r => r.ToResponse()).ToList()));
        });

        g.MapGet("/plans/{id:guid}", async (Guid id, PlanRepository repo) =>
        {
            var row = await repo.GetAsync(id);
            return row is null ? NotFound() : Results.Ok(new DataEnvelope<PlanResponse>(row.ToResponse()));
        });

        g.MapPost("/plans", async (PlanUpsertRequest req, PlanRepository repo) =>
        {
            var row = await repo.UpsertAsync(req);
            return Results.Json(new DataEnvelope<PlanResponse>(row!.ToResponse()), statusCode: 201);
        });

        return app;
    }
}
