using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Sms.Modules.Academics.Contracts;
using Sms.Modules.Academics.Data;
using Sms.Shared.Kernel.Http;
using Sms.Shared.Kernel.Results;
using Sms.Shared.Kernel.Tenancy;

namespace Sms.Modules.Academics;

public static class AcademicsModule
{
    public static IServiceCollection AddAcademicsModule(this IServiceCollection services)
    {
        services.AddScoped<ClassRepository>();
        services.AddScoped<SubjectRepository>();
        services.AddScoped<AttendanceRepository>();
        return services;
    }

    private static IResult NotFound() =>
        Results.Json(ErrorEnvelope.From(new Error("not_found", "resource not found")), statusCode: 404);

    private static IResult Forbidden(string message) =>
        Results.Json(ErrorEnvelope.From(new Error("forbidden", message)), statusCode: 403);

    /// Phase 2 academics: /v1/classes, /v1/subjects, roll-call attendance (TVP bulk upsert). Tenant-scoped.
    public static IEndpointRouteBuilder MapAcademicsModule(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/v1").RequireAuthorization();

        // ---- Classes ----
        g.MapGet("/classes", async (ClassRepository repo) =>
            Results.Ok(new DataEnvelope<IReadOnlyList<ClassResponse>>(await repo.ListAsync())));

        g.MapGet("/classes/{id:guid}", async (Guid id, ClassRepository repo) =>
        {
            var c = await repo.GetAsync(id);
            return c is null ? NotFound() : Results.Ok(new DataEnvelope<ClassResponse>(c));
        });

        g.MapPost("/classes", async (CreateClassRequest req, ClassRepository repo, ITenantContext tenant) =>
        {
            if (tenant.TenantId is not { } tid) return Forbidden("no tenant context");
            return Results.Json(new DataEnvelope<ClassResponse>((await repo.CreateAsync(tid, req))!), statusCode: 201);
        });

        // ---- Subjects ----
        g.MapGet("/subjects", async (SubjectRepository repo) =>
            Results.Ok(new DataEnvelope<IReadOnlyList<SubjectResponse>>(await repo.ListAsync())));

        g.MapGet("/subjects/{id:guid}", async (Guid id, SubjectRepository repo) =>
        {
            var s = await repo.GetAsync(id);
            return s is null ? NotFound() : Results.Ok(new DataEnvelope<SubjectResponse>(s));
        });

        g.MapPost("/subjects", async (CreateSubjectRequest req, SubjectRepository repo, ITenantContext tenant) =>
        {
            if (tenant.TenantId is not { } tid) return Forbidden("no tenant context");
            return Results.Json(new DataEnvelope<SubjectResponse>((await repo.CreateAsync(tid, req))!), statusCode: 201);
        });

        // ---- Roll-call attendance ----
        g.MapGet("/classes/{classId:guid}/attendance", async (Guid classId, DateTime date, AttendanceRepository repo) =>
            Results.Ok(new DataEnvelope<IReadOnlyList<AttendanceRecordResponse>>(await repo.ListAsync(classId, date))));

        g.MapPost("/classes/{classId:guid}/attendance",
            async (Guid classId, BulkAttendanceRequest req, AttendanceRepository repo, ITenantContext tenant) =>
        {
            if (tenant.TenantId is not { } tid) return Forbidden("no tenant context");
            await repo.BulkUpsertAsync(tid, classId, req.Date, tenant.UserId, req.Records);
            return Results.NoContent();
        });

        return app;
    }
}
