using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Sms.Modules.Sis.Contracts;
using Sms.Modules.Sis.Data;
using Sms.Shared.Kernel.Authz;
using Sms.Shared.Kernel.Http;
using Sms.Shared.Kernel.Results;
using Sms.Shared.Kernel.Tenancy;

namespace Sms.Modules.Sis;

public static class SisModule
{
    public static IServiceCollection AddSisModule(this IServiceCollection services)
    {
        services.AddScoped<StudentRepository>();
        return services;
    }

    private static IResult NotFound() =>
        Results.Json(ErrorEnvelope.From(new Error("not_found", "resource not found")), statusCode: 404);

    private static IResult Forbidden(string message) =>
        Results.Json(ErrorEnvelope.From(new Error("forbidden", message)), statusCode: 403);

    /// Phase 2 SIS: /v1/students. Tenant-scoped — every row is RLS-filtered to the caller's school.
    public static IEndpointRouteBuilder MapSisModule(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/v1").RequireAuthorization();

        g.MapGet("/students", async (StudentRepository repo, string? q, string? grade, string? status, string? fee) =>
            Results.Ok(new CursorPage<StudentResponse>(await repo.ListAsync(q, grade, status, fee), null)));

        g.MapGet("/students/{id:guid}", async (Guid id, StudentRepository repo) =>
        {
            var s = await repo.GetAsync(id);
            return s is null ? NotFound() : Results.Ok(new DataEnvelope<StudentResponse>(s));
        });

        g.MapPost("/students", async (CreateStudentRequest req, StudentRepository repo, ITenantContext tenant) =>
        {
            if (tenant.TenantId is not { } tid) return Forbidden("no tenant context");
            return Results.Json(new DataEnvelope<StudentResponse>((await repo.CreateAsync(tid, req))!), statusCode: 201);
        });

        g.MapPatch("/students/{id:guid}", async (Guid id, UpdateStudentRequest req, StudentRepository repo) =>
        {
            if (await repo.GetAsync(id) is null) return NotFound();
            return Results.Ok(new DataEnvelope<StudentResponse>((await repo.UpdateAsync(id, req))!));
        });

        g.MapGet("/classes/{classId:guid}/students", async (
            Guid classId, StudentRepository repo,
            [FromQuery] int? limit, [FromQuery] string? cursor) =>
        {
            var page = new PageRequest(limit ?? 50, cursor);
            var (rows, next) = await repo.ListByClassPagedAsync(classId, page.SafeLimit, page.Cursor);
            return Results.Ok(new CursorPage<StudentResponse>(rows, next));
        }).RequireAuthorization(AuthorizationPolicies.TeacherApp);

        return app;
    }
}
