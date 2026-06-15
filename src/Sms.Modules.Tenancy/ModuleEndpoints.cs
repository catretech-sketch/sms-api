using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
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
        services.AddScoped<InvoiceRepository>();
        services.AddScoped<SubscriptionRepository>();
        services.AddScoped<DashboardRepository>();
        services.AddScoped<OnboardingRepository>();
        services.AddScoped<TicketRepository>();
        services.AddScoped<TeamRepository>();
        services.AddScoped<AuditRepository>();
        services.AddScoped<ReportRepository>();
        return services;
    }

    private static IResult NotFound() =>
        Results.Json(ErrorEnvelope.From(new Error("not_found", "resource not found")), statusCode: 404);

    private static IResult Conflict(string message) =>
        Results.Json(ErrorEnvelope.From(new Error("conflict", message)), statusCode: 409);

    private static string Csv(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        return value.Contains(',') || value.Contains('"') || value.Contains('\n')
            ? "\"" + value.Replace("\"", "\"\"") + "\""
            : value;
    }

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

        g.MapPost("/clients", async (CreateClientRequest req, ClientRepository repo,
            Sms.Shared.Kernel.Auth.UserProvisioningRepository users) =>
        {
            var row = await repo.CreateAsync(req);
            if (row is not null && (req.AdminEmail is not null || req.AdminPhone is not null))
                await users.CreateUserAsync(row.Id, req.AdminEmail, req.AdminPhone, false,
                    new[] { Sms.Shared.Kernel.Authz.Policies.SchoolAdmin });
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

        // ---- Invoices ----
        g.MapGet("/invoices", async (InvoiceRepository repo, string? status,
            [FromQuery(Name = "tenant_id")] Guid? tenantId) =>
            Results.Ok(new CursorPage<InvoiceResponse>(await repo.ListAsync(status, tenantId), null)));

        g.MapGet("/invoices/{id:guid}", async (Guid id, InvoiceRepository repo) =>
        {
            var inv = await repo.GetAsync(id);
            return inv is null ? NotFound() : Results.Ok(new DataEnvelope<InvoiceResponse>(inv));
        });

        g.MapPost("/invoices/{id:guid}/mark-paid", async (Guid id, InvoiceRepository repo) =>
        {
            if (await repo.GetAsync(id) is null) return NotFound();
            return Results.Ok(new DataEnvelope<InvoiceResponse>((await repo.MarkPaidAsync(id))!));
        });

        g.MapPost("/invoices/{id:guid}/refund", async (Guid id, InvoiceRepository repo) =>
        {
            var inv = await repo.GetAsync(id);
            if (inv is null) return NotFound();
            if (inv.Status != "paid") return Conflict("invoice is not paid");
            return Results.Ok(new DataEnvelope<InvoiceResponse>((await repo.RefundAsync(id))!));
        });

        // ---- Subscriptions ----
        g.MapGet("/subscriptions", async (SubscriptionRepository repo, string? status,
            [FromQuery(Name = "tenant_id")] Guid? tenantId) =>
            Results.Ok(new CursorPage<SubscriptionResponse>(await repo.ListAsync(tenantId, status), null)));

        g.MapGet("/subscriptions/{id:guid}", async (Guid id, SubscriptionRepository repo) =>
        {
            var sub = await repo.GetAsync(id);
            return sub is null ? NotFound() : Results.Ok(new DataEnvelope<SubscriptionResponse>(sub));
        });

        g.MapPost("/subscriptions", async (CreateSubscriptionRequest req, SubscriptionRepository repo) =>
            Results.Json(new DataEnvelope<SubscriptionResponse>((await repo.CreateAsync(req))!), statusCode: 201));

        // ---- Dashboard ----
        g.MapGet("/dashboard/overview", async (DashboardRepository repo) =>
            Results.Ok(new DataEnvelope<DashboardOverview>(await repo.OverviewAsync())));

        // ---- Onboarding ----
        g.MapGet("/onboarding", async (OnboardingRepository repo, string? stage) =>
            Results.Ok(new DataEnvelope<IReadOnlyList<OnboardingItemResponse>>(await repo.ListAsync(stage))));

        g.MapPost("/onboarding", async (CreateOnboardingRequest req, OnboardingRepository repo) =>
        {
            var id = await repo.CreateAsync(req);
            return Results.Json(new DataEnvelope<OnboardingItemResponse>((await repo.GetAsync(id))!), statusCode: 201);
        });

        g.MapPost("/onboarding/{id:guid}/advance", async (Guid id, AdvanceRequest req, OnboardingRepository repo) =>
        {
            if (await repo.GetAsync(id) is null) return NotFound();
            await repo.AdvanceAsync(id, req.Stage);
            return Results.Ok(new DataEnvelope<OnboardingItemResponse>((await repo.GetAsync(id))!));
        });

        g.MapPatch("/onboarding/{id:guid}/checklist", async (Guid id, ChecklistRequest req, OnboardingRepository repo) =>
        {
            if (await repo.GetAsync(id) is null) return NotFound();
            await repo.SetChecklistAsync(id, req.Label, req.Done);
            return Results.Ok(new DataEnvelope<OnboardingItemResponse>((await repo.GetAsync(id))!));
        });

        // ---- Support tickets ----
        g.MapGet("/tickets", async (TicketRepository repo, string? status, string? q) =>
            Results.Ok(new CursorPage<TicketResponse>(await repo.ListAsync(status, q), null)));

        g.MapGet("/tickets/{id:guid}", async (Guid id, TicketRepository repo) =>
        {
            var detail = await repo.GetDetailAsync(id);
            return detail is null ? NotFound() : Results.Ok(new DataEnvelope<TicketDetailResponse>(detail));
        });

        g.MapPost("/tickets", async (CreateTicketRequest req, TicketRepository repo) =>
        {
            var id = await repo.CreateAsync(req);
            return Results.Json(new DataEnvelope<TicketDetailResponse>((await repo.GetDetailAsync(id))!), statusCode: 201);
        });

        g.MapPatch("/tickets/{id:guid}", async (Guid id, UpdateTicketRequest req, TicketRepository repo) =>
        {
            if (await repo.GetDetailAsync(id) is null) return NotFound();
            return Results.Ok(new DataEnvelope<TicketResponse>((await repo.UpdateAsync(id, req.Status, req.Assignee))!));
        });

        g.MapPost("/tickets/{id:guid}/messages", async (Guid id, AddMessageRequest req, HttpContext http, TicketRepository repo) =>
        {
            if (await repo.GetDetailAsync(id) is null) return NotFound();
            var who = http.User.FindFirst("sub")?.Value ?? "agent";
            await repo.AddMessageAsync(id, who, req.Text);
            return Results.Json(new DataEnvelope<TicketDetailResponse>((await repo.GetDetailAsync(id))!), statusCode: 201);
        });

        // ---- Team ----
        g.MapGet("/team", async (TeamRepository repo) =>
            Results.Ok(new DataEnvelope<IReadOnlyList<TeamMemberResponse>>(await repo.ListAsync())));

        g.MapPost("/team", async (InviteTeamRequest req, TeamRepository repo) =>
            Results.Json(new DataEnvelope<TeamMemberResponse>((await repo.InviteAsync(req))!), statusCode: 201));

        g.MapPatch("/team/{id:guid}", async (Guid id, UpdateTeamRequest req, TeamRepository repo) =>
        {
            var updated = await repo.UpdateAsync(id, req.Role, req.Status);
            return updated is null ? NotFound() : Results.Ok(new DataEnvelope<TeamMemberResponse>(updated));
        });

        // ---- Audit ----
        g.MapGet("/audit", async (AuditRepository repo, string? kind,
            [FromQuery(Name = "actor_id")] Guid? actorId, [FromQuery(Name = "tenant_id")] Guid? tenantId) =>
            Results.Ok(new CursorPage<AuditEntry>(await repo.ListAsync(kind, actorId, tenantId), null)));

        // ---- Reports ----
        g.MapGet("/reports/revenue", async (ReportRepository repo) =>
            Results.Ok(new DataEnvelope<RevenueReport>(await repo.RevenueAsync())));

        g.MapGet("/reports/clients.csv", async (ClientRepository repo, HttpResponse resp) =>
        {
            var rows = await repo.ListAsync(null, null, null);
            var sb = new System.Text.StringBuilder("client,status,plan,mrr,students,staff,country,created\n");
            foreach (var r in rows)
                sb.Append(Csv(r.Name)).Append(',').Append(r.Status).Append(',').Append(Csv(r.PlanName)).Append(',')
                  .Append(r.Mrr).Append(',').Append(r.StudentsCount).Append(',').Append(r.StaffCount).Append(',')
                  .Append(Csv(r.Country)).Append(',').Append(r.CreatedAt.ToString("yyyy-MM-dd")).Append('\n');
            resp.Headers.ContentDisposition = "attachment; filename=\"catre-clients.csv\"";
            return Results.Text(sb.ToString(), "text/csv");
        });

        return app;
    }
}
