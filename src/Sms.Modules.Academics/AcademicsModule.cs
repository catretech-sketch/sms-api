using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Sms.Modules.Academics.Contracts;
using Sms.Modules.Academics.Data;
using Sms.Shared.Kernel.Http;
using Sms.Shared.Kernel.Results;
using Sms.Shared.Kernel.Authz;
using Sms.Shared.Kernel.Tenancy;
using Sms.Shared.Kernel.Time;

namespace Sms.Modules.Academics;

public static class AcademicsModule
{
    public static IServiceCollection AddAcademicsModule(this IServiceCollection services)
    {
        services.AddScoped<ClassRepository>();
        services.AddScoped<SubjectRepository>();
        services.AddScoped<AttendanceRepository>();
        services.AddScoped<ExamRepository>();
        services.AddScoped<HomeworkRepository>();
        services.AddScoped<TimetableRepository>();
        services.AddScoped<CalendarRepository>();
        services.AddScoped<LibraryRepository>();
        services.AddScoped<AssignmentRepository>();
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

        // ---- Exams (term) ----
        g.MapGet("/exams", async (ExamRepository repo) =>
            Results.Ok(new DataEnvelope<IReadOnlyList<ExamResponse>>(await repo.ListExamsAsync())));

        g.MapGet("/exams/{id:guid}", async (Guid id, ExamRepository repo) =>
        {
            var e = await repo.GetExamAsync(id);
            return e is null ? NotFound() : Results.Ok(new DataEnvelope<ExamResponse>(e));
        });

        g.MapPost("/exams", async (CreateExamRequest req, ExamRepository repo, ITenantContext tenant) =>
        {
            if (tenant.TenantId is not { } tid) return Forbidden("no tenant context");
            return Results.Json(new DataEnvelope<ExamResponse>((await repo.CreateExamAsync(tid, req))!), statusCode: 201);
        });

        g.MapPatch("/exams/{id:guid}", async (Guid id, UpdateExamRequest req, ExamRepository repo) =>
        {
            if (await repo.GetExamAsync(id) is null) return NotFound();
            return Results.Ok(new DataEnvelope<ExamResponse>((await repo.UpdateExamAsync(id, req))!));
        });

        // ---- Exam papers (shared resource) ----
        g.MapGet("/exam-papers", async (ExamRepository repo,
            [FromQuery(Name = "exam_id")] Guid? examId) =>
            Results.Ok(new DataEnvelope<IReadOnlyList<ExamPaperResponse>>(await repo.ListExamPapersAsync(examId))));

        g.MapGet("/exam-papers/{id:guid}", async (Guid id, ExamRepository repo) =>
        {
            var p = await repo.GetExamPaperAsync(id);
            return p is null ? NotFound() : Results.Ok(new DataEnvelope<ExamPaperResponse>(p));
        });

        g.MapPost("/exam-papers", async (CreateExamPaperRequest req, ExamRepository repo, ITenantContext tenant) =>
        {
            if (tenant.TenantId is not { } tid) return Forbidden("no tenant context");
            return Results.Json(new DataEnvelope<ExamPaperResponse>((await repo.CreateExamPaperAsync(tid, req))!), statusCode: 201);
        });

        g.MapPatch("/exam-papers/{id:guid}", async (Guid id, UpdateExamPaperRequest req, ExamRepository repo) =>
        {
            var updated = await repo.UpdateExamPaperAsync(id, req);
            return updated is null ? NotFound() : Results.Ok(new DataEnvelope<ExamPaperResponse>(updated));
        }).RequireAuthorization(AuthorizationPolicies.TeacherApp);

        g.MapDelete("/exam-papers/{id:guid}", async (Guid id, ExamRepository repo) =>
        {
            await repo.DeleteExamPaperAsync(id);
            return Results.NoContent();
        }).RequireAuthorization(AuthorizationPolicies.TeacherApp);

        // ---- Grades ----
        g.MapGet("/exam-papers/{examPaperId:guid}/grades", async (Guid examPaperId, ExamRepository repo) =>
            Results.Ok(new DataEnvelope<IReadOnlyList<GradeResponse>>(await repo.ListGradesAsync(examPaperId))));

        g.MapPut("/grades", async (UpsertGradeRequest req, ExamRepository repo, ITenantContext tenant) =>
        {
            if (tenant.TenantId is not { } tid) return Forbidden("no tenant context");
            return Results.Ok(new DataEnvelope<GradeResponse>((await repo.UpsertGradeAsync(tid, req))!));
        });

        // ---- Homework (student) ----
        g.MapGet("/homework", async (HomeworkRepository repo,
            [FromQuery(Name = "student_id")] Guid? studentId, string? status) =>
            Results.Ok(new DataEnvelope<IReadOnlyList<HomeworkResponse>>(await repo.ListAsync(studentId, status))));

        g.MapGet("/homework/{id:guid}", async (Guid id, HomeworkRepository repo) =>
        {
            var h = await repo.GetAsync(id);
            return h is null ? NotFound() : Results.Ok(new DataEnvelope<HomeworkResponse>(h));
        });

        g.MapPost("/homework", async (CreateHomeworkRequest req, HomeworkRepository repo, ITenantContext tenant) =>
        {
            if (tenant.TenantId is not { } tid) return Forbidden("no tenant context");
            return Results.Json(new DataEnvelope<HomeworkResponse>((await repo.CreateAsync(tid, req))!), statusCode: 201);
        });

        g.MapPatch("/homework/{id:guid}", async (Guid id, SetHomeworkStatusRequest req, HomeworkRepository repo) =>
        {
            if (await repo.GetAsync(id) is null) return NotFound();
            return Results.Ok(new DataEnvelope<HomeworkResponse>((await repo.SetStatusAsync(id, req.Status))!));
        });

        g.MapPost("/homework/{id:guid}/submit", async (Guid id, HomeworkRepository repo) =>
        {
            if (await repo.GetAsync(id) is null) return NotFound();
            return Results.Ok(new DataEnvelope<HomeworkResponse>((await repo.SetStatusAsync(id, "submitted"))!));
        });

        // ---- Timetable ----
        g.MapGet("/timetable", async (TimetableRepository repo) =>
            Results.Ok(new DataEnvelope<IReadOnlyList<TimetableSlotResponse>>(await repo.ListAsync())))
            .RequireAuthorization(AuthorizationPolicies.TeacherApp);

        g.MapPost("/timetable", async (CreateTimetableSlotRequest req, TimetableRepository repo, ITenantContext tenant) =>
        {
            if (tenant.TenantId is not { } tid) return Forbidden("no tenant context");
            return Results.Json(new DataEnvelope<TimetableSlotResponse>((await repo.CreateAsync(tid, req))!), statusCode: 201);
        }).RequireAuthorization(Policies.Principal);

        // ---- Calendar ----
        g.MapGet("/calendar", async (CalendarRepository repo) =>
            Results.Ok(new DataEnvelope<IReadOnlyList<CalendarEventResponse>>(await repo.ListAsync())))
            .RequireAuthorization(AuthorizationPolicies.TeacherApp);

        g.MapPost("/calendar", async (CreateCalendarEventRequest req, CalendarRepository repo, ITenantContext tenant) =>
        {
            if (tenant.TenantId is not { } tid) return Forbidden("no tenant context");
            return Results.Json(new DataEnvelope<CalendarEventResponse>((await repo.CreateAsync(tid, req))!), statusCode: 201);
        }).RequireAuthorization(Policies.Principal);

        // ---- Library ----
        g.MapGet("/library", async (LibraryRepository repo, IClock clock) =>
            Results.Ok(new DataEnvelope<IReadOnlyList<LibraryBookResponse>>(await repo.ListAsync(clock.UtcNow))))
            .RequireAuthorization(AuthorizationPolicies.TeacherApp);

        g.MapPost("/library", async (CreateLibraryBookRequest req, LibraryRepository repo, ITenantContext tenant) =>
        {
            if (tenant.TenantId is not { } tid) return Forbidden("no tenant context");
            return Results.Json(new DataEnvelope<LibraryBookResponse>((await repo.CreateAsync(tid, req))!), statusCode: 201);
        }).RequireAuthorization(Policies.Principal);

        // ---- Assignments ----
        g.MapGet("/assignments", async (AssignmentRepository repo, IClock clock) =>
            Results.Ok(new DataEnvelope<IReadOnlyList<AssignmentResponse>>(await repo.ListAsync(clock.UtcNow))))
            .RequireAuthorization(AuthorizationPolicies.TeacherApp);

        g.MapPost("/assignments", async (CreateAssignmentRequest req, AssignmentRepository repo, ITenantContext tenant) =>
        {
            if (tenant.TenantId is not { } tid) return Forbidden("no tenant context");
            return Results.Json(new DataEnvelope<AssignmentResponse>((await repo.CreateAsync(tid, req))!), statusCode: 201);
        }).RequireAuthorization(AuthorizationPolicies.TeacherApp);

        return app;
    }
}
