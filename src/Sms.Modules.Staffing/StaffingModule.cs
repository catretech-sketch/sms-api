using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Sms.Modules.Staffing.Contracts;
using Sms.Modules.Staffing.Data;
using Sms.Shared.Kernel.Authz;
using Sms.Shared.Kernel.Http;
using Sms.Shared.Kernel.Results;
using Sms.Shared.Kernel.Tenancy;

namespace Sms.Modules.Staffing;

public static class StaffingModule
{
    public static IServiceCollection AddStaffingModule(this IServiceCollection services)
    {
        services.AddScoped<TeacherRepository>();
        services.AddScoped<StaffRepository>();
        services.AddScoped<LeaveRepository>();
        return services;
    }

    private static IResult NotFound() =>
        Results.Json(ErrorEnvelope.From(new Error("not_found", "resource not found")), statusCode: 404);

    private static IResult Forbidden(string message) =>
        Results.Json(ErrorEnvelope.From(new Error("forbidden", message)), statusCode: 403);

    /// Phase 2 staffing: /v1/teachers + /v1/staff. Tenant-scoped (RLS-isolated per school).
    public static IEndpointRouteBuilder MapStaffingModule(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/v1").RequireAuthorization();

        // ---- Teachers ----
        g.MapGet("/teachers", async (TeacherRepository repo, string? q, string? dept, string? status) =>
            Results.Ok(new CursorPage<TeacherResponse>(await repo.ListAsync(q, dept, status), null)));

        g.MapGet("/teachers/{id:guid}", async (Guid id, TeacherRepository repo) =>
        {
            var t = await repo.GetAsync(id);
            return t is null ? NotFound() : Results.Ok(new DataEnvelope<TeacherResponse>(t));
        });

        g.MapPost("/teachers", async (CreateTeacherRequest req, TeacherRepository repo, ITenantContext tenant) =>
        {
            if (tenant.TenantId is not { } tid) return Forbidden("no tenant context");
            return Results.Json(new DataEnvelope<TeacherResponse>((await repo.CreateAsync(tid, req))!), statusCode: 201);
        });

        g.MapPatch("/teachers/{id:guid}", async (Guid id, UpdateTeacherRequest req, TeacherRepository repo) =>
        {
            if (await repo.GetAsync(id) is null) return NotFound();
            return Results.Ok(new DataEnvelope<TeacherResponse>((await repo.UpdateAsync(id, req))!));
        });

        // ---- Staff ----
        g.MapGet("/staff", async (StaffRepository repo, string? q, string? cat) =>
            Results.Ok(new CursorPage<StaffResponse>(await repo.ListAsync(q, cat), null)));

        g.MapGet("/staff/{id:guid}", async (Guid id, StaffRepository repo) =>
        {
            var s = await repo.GetAsync(id);
            return s is null ? NotFound() : Results.Ok(new DataEnvelope<StaffResponse>(s));
        });

        g.MapPost("/staff", async (CreateStaffRequest req, StaffRepository repo, ITenantContext tenant) =>
        {
            if (tenant.TenantId is not { } tid) return Forbidden("no tenant context");
            return Results.Json(new DataEnvelope<StaffResponse>((await repo.CreateAsync(tid, req))!), statusCode: 201);
        });

        g.MapPatch("/staff/{id:guid}", async (Guid id, UpdateStaffRequest req, StaffRepository repo) =>
        {
            if (await repo.GetAsync(id) is null) return NotFound();
            return Results.Ok(new DataEnvelope<StaffResponse>((await repo.UpdateAsync(id, req))!));
        });

        // ---- Leave (file) + Approvals (inbox) ----
        g.MapGet("/leave", async (LeaveRepository repo, ITenantContext tenant) =>
            Results.Ok(new DataEnvelope<IReadOnlyList<LeaveResponse>>(await repo.ListMineAsync(tenant.UserId))));

        g.MapPost("/leave", async (CreateLeaveRequest req, LeaveRepository repo, ITenantContext tenant) =>
        {
            if (tenant.TenantId is not { } tid) return Forbidden("no tenant context");
            return Results.Json(new DataEnvelope<LeaveResponse>((await repo.CreateAsync(tid, tenant.UserId, req))!), statusCode: 201);
        });

        g.MapGet("/approvals", async (LeaveRepository repo, string? status) =>
            Results.Ok(new DataEnvelope<IReadOnlyList<LeaveResponse>>(await repo.ListByStatusAsync(status ?? "pending"))))
            .RequireAuthorization(Policies.Principal);

        g.MapPatch("/approvals/{id:guid}", async (Guid id, DecideLeaveRequest req, LeaveRepository repo, ITenantContext tenant) =>
        {
            if (await repo.GetAsync(id) is null) return NotFound();
            return Results.Ok(new DataEnvelope<LeaveResponse>((await repo.DecideAsync(id, req.Status, tenant.UserId, req.DecidedNote))!));
        })
            .RequireAuthorization(Policies.Principal);

        return app;
    }
}
