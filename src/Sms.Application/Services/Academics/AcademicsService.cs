using System.Security.Claims;
using Sms.Application.Common;
using Sms.Application.Services.Sis;
using Sms.Modules.Academics.Contracts;
using Sms.Modules.Academics.Data;
using Sms.Shared.Kernel.Authz;
using Sms.Shared.Kernel.Results;
using Sms.Shared.Kernel.Tenancy;
using Sms.Shared.Kernel.Time;

namespace Sms.Application.Services.Academics;

public sealed class AcademicsService(
    ClassRepository classes,
    SubjectRepository subjects,
    AttendanceRepository attendance,
    StaffAttendanceRepository staffAttendance,
    ExamRepository exams,
    HomeworkRepository homework,
    TimetableRepository timetable,
    CalendarRepository calendar,
    LibraryRepository library,
    AssignmentRepository assignments,
    AcademicsCommsNotifier academicsNotifier,
    ISisService sis,
    ITenantContext tenant,
    ITenantFeatureSet features,
    IClock clock) : IAcademicsService
{
    private static string? NormPersonType(string? t)
    {
        var v = (t ?? "").Trim().ToLowerInvariant();
        return v is "teacher" or "staff" ? v : null;
    }

    private static bool IsLeadership(ClaimsPrincipal caller) =>
        caller.FindAll("role")
            .Select(c => c.Value.Split('.').LastOrDefault())
            .Any(r => r is "principal" or "admin" or "owner");

    private static bool IsStudentOrParent(ClaimsPrincipal caller) =>
        caller.FindAll("role")
            .Select(c => c.Value.Trim().ToLowerInvariant())
            .Any(r => r == Policies.StudentOrParent ||
                r.Split('.').LastOrDefault() is "student" or "parent");

    // Only narrows for a caller whose SOLE role is "teacher" — principal/admin/owner tokens
    // (however else combined) keep seeing every class, matching sms-admin's and the
    // principal screens' existing expectation of an unscoped list.
    public async Task<ApiResult<IReadOnlyList<ClassResponse>>> ListClassesAsync(
        ClaimsPrincipal caller, CancellationToken ct = default)
    {
        var roles = caller.FindAll("role").Select(c => c.Value.Split('.').LastOrDefault()).ToArray();
        var isTeacherOnly = roles.Contains("teacher") &&
            !roles.Any(r => r is "principal" or "admin" or "owner");
        if (!isTeacherOnly)
            return ApiResult<IReadOnlyList<ClassResponse>>.Ok(await classes.ListAsync(ct));

        var sub = caller.FindFirst("sub")?.Value;
        if (sub is null || !Guid.TryParse(sub, out var userId))
            return ApiResult<IReadOnlyList<ClassResponse>>.Fail(new Error("unauthorized", "unauthorized"), 401);
        return ApiResult<IReadOnlyList<ClassResponse>>.Ok(await classes.ListForTeacherAsync(userId, ct));
    }

    public async Task<ApiResult<ClassResponse>> GetClassAsync(Guid id, CancellationToken ct = default)
    {
        var c = await classes.GetAsync(id, ct);
        return c is null
            ? ApiResult<ClassResponse>.Fail(new Error("not_found", "resource not found"), 404)
            : ApiResult<ClassResponse>.Ok(c);
    }

    public async Task<ApiResult<ClassResponse>> CreateClassAsync(CreateClassRequest req, CancellationToken ct = default)
    {
        if (tenant.TenantId is not { } tid)
            return ApiResult<ClassResponse>.Fail(new Error("forbidden", "no tenant context"), 403);
        return ApiResult<ClassResponse>.Ok((await classes.CreateAsync(tid, req, ct))!, 201);
    }

    public async Task<ApiResult<ClassResponse>> UpdateClassAsync(Guid id, UpdateClassRequest req, CancellationToken ct = default)
    {
        if (tenant.TenantId is not { } tid)
            return ApiResult<ClassResponse>.Fail(new Error("forbidden", "no tenant context"), 403);
        if (await classes.GetAsync(id, ct) is null)
            return ApiResult<ClassResponse>.Fail(new Error("not_found", "resource not found"), 404);
        var updated = await classes.UpdateAsync(id, tid, req, ct);
        return updated is null
            ? ApiResult<ClassResponse>.Fail(new Error("not_found", "resource not found"), 404)
            : ApiResult<ClassResponse>.Ok(updated);
    }

    public async Task<ApiResult<IReadOnlyList<SubjectResponse>>> ListSubjectsAsync(CancellationToken ct = default) =>
        ApiResult<IReadOnlyList<SubjectResponse>>.Ok(await subjects.ListAsync(ct));

    public async Task<ApiResult<SubjectResponse>> GetSubjectAsync(Guid id, CancellationToken ct = default)
    {
        var s = await subjects.GetAsync(id, ct);
        return s is null
            ? ApiResult<SubjectResponse>.Fail(new Error("not_found", "resource not found"), 404)
            : ApiResult<SubjectResponse>.Ok(s);
    }

    public async Task<ApiResult<SubjectResponse>> CreateSubjectAsync(CreateSubjectRequest req, CancellationToken ct = default)
    {
        if (tenant.TenantId is not { } tid)
            return ApiResult<SubjectResponse>.Fail(new Error("forbidden", "no tenant context"), 403);
        return ApiResult<SubjectResponse>.Ok((await subjects.CreateAsync(tid, req, ct))!, 201);
    }

    public async Task<ApiResult<SubjectResponse>> UpdateSubjectAsync(Guid id, UpdateSubjectRequest req, CancellationToken ct = default)
    {
        if (tenant.TenantId is not { } tid)
            return ApiResult<SubjectResponse>.Fail(new Error("forbidden", "no tenant context"), 403);
        if (await subjects.GetAsync(id, ct) is null)
            return ApiResult<SubjectResponse>.Fail(new Error("not_found", "resource not found"), 404);
        var updated = await subjects.UpdateAsync(id, tid, req, ct);
        return updated is null
            ? ApiResult<SubjectResponse>.Fail(new Error("not_found", "resource not found"), 404)
            : ApiResult<SubjectResponse>.Ok(updated);
    }

    public async Task<ApiResult> DeleteSubjectAsync(Guid id, CancellationToken ct = default)
    {
        if (tenant.TenantId is not { } tid)
            return ApiResult.Fail(new Error("forbidden", "no tenant context"), 403);
        if (await subjects.GetAsync(id, ct) is null)
            return ApiResult.Fail(new Error("not_found", "resource not found"), 404);
        await subjects.DeleteAsync(id, tid, ct);
        return ApiResult.NoContent();
    }

    public async Task<ApiResult<IReadOnlyList<AttendanceRecordResponse>>> ListAttendanceAsync(
        Guid classId, DateTime date, CancellationToken ct = default) =>
        ApiResult<IReadOnlyList<AttendanceRecordResponse>>.Ok(await attendance.ListAsync(classId, date, ct));

    public async Task<ApiResult> BulkUpsertAttendanceAsync(
        Guid classId, BulkAttendanceRequest req, ClaimsPrincipal caller, CancellationToken ct = default)
    {
        if (tenant.TenantId is not { } tid)
            return ApiResult.Fail(new Error("forbidden", "no tenant context"), 403);

        var classRow = await classes.GetAsync(classId, ct);
        if (classRow is null)
            return ApiResult.Fail(new Error("not_found", "resource not found"), 404);

        var day = AttendanceRollCall.DayKey(req.Date);
        var slotRows = await timetable.ListForClassDayAsync(classId, day, ct);
        var firstSlot = AttendanceRollCall.FirstTeachingSlot(slotRows.Select(s =>
            new AttendanceRollCall.SlotInput(
                day, s.Period, s.Subject, classId, s.TeacherId,
                s.StartTime, s.EndTime, s.TeacherName)));
        var callerTeacherId = tenant.UserId is { } userId
            ? await classes.TeacherIdForUserAsync(userId, ct)
            : null;
        if (!AttendanceRollCall.CanMark(
                IsLeadership(caller), callerTeacherId, classRow.ClassTeacherId, firstSlot?.TeacherId))
        {
            return ApiResult.Fail(
                new Error(
                    "not_roll_call_teacher",
                    "only the class teacher, first-period teacher, or leadership can mark this day"),
                403);
        }

        await attendance.BulkUpsertAsync(tid, classId, req.Date, tenant.UserId, req.Records, ct);
        return ApiResult.NoContent();
    }

    public async Task<ApiResult<AttendanceRollCallResponse>> GetAttendanceRollCallAsync(
        Guid classId, DateTime date, ClaimsPrincipal caller, CancellationToken ct = default)
    {
        if (tenant.TenantId is null)
            return ApiResult<AttendanceRollCallResponse>.Fail(
                new Error("forbidden", "no tenant context"), 403);

        var classRow = await classes.GetAsync(classId, ct);
        if (classRow is null)
            return ApiResult<AttendanceRollCallResponse>.Fail(
                new Error("not_found", "resource not found"), 404);

        var day = AttendanceRollCall.DayKey(date);
        var slotRows = await timetable.ListForClassDayAsync(classId, day, ct);
        var firstSlot = AttendanceRollCall.FirstTeachingSlot(slotRows.Select(s =>
            new AttendanceRollCall.SlotInput(
                day, s.Period, s.Subject, classId, s.TeacherId,
                s.StartTime, s.EndTime, s.TeacherName)));
        var callerTeacherId = tenant.UserId is { } userId
            ? await classes.TeacherIdForUserAsync(userId, ct)
            : null;
        var leadership = IsLeadership(caller);
        var canMark = AttendanceRollCall.CanMark(
            leadership, callerTeacherId, classRow.ClassTeacherId, firstSlot?.TeacherId);
        var reason = AttendanceRollCall.Reason(
            leadership, callerTeacherId, classRow.ClassTeacherId, firstSlot?.TeacherId);
        var classTeacherName = classRow.ClassTeacherId is { } classTeacherId
            ? await classes.TeacherNameAsync(classTeacherId, ct)
            : null;
        var marked = (await attendance.ListAsync(classId, date, ct)).Count > 0;

        return ApiResult<AttendanceRollCallResponse>.Ok(new AttendanceRollCallResponse(
            date.Date,
            day,
            firstSlot?.Period,
            firstSlot?.Subject,
            firstSlot?.StartTime,
            firstSlot?.EndTime,
            firstSlot?.TeacherId,
            firstSlot?.TeacherName,
            classRow.ClassTeacherId,
            classTeacherName,
            canMark,
            reason,
            marked));
    }

    public async Task<ApiResult<IReadOnlyList<AttendanceRecordResponse>>> ListAttendanceForStudentAsync(
        Guid studentId, DateTime from, DateTime to, ClaimsPrincipal caller, CancellationToken ct = default)
    {
        if (IsStudentOrParent(caller))
        {
            var me = await sis.GetMyStudentAsync(ct);
            if (me.Data?.Id != studentId)
                return ApiResult<IReadOnlyList<AttendanceRecordResponse>>.Fail(
                    new Error("not_own_student", "students and parents can only read their linked student's attendance"),
                    403);
        }

        return ApiResult<IReadOnlyList<AttendanceRecordResponse>>.Ok(
            await attendance.ListForStudentAsync(studentId, from, to, ct));
    }

    public async Task<ApiResult<IReadOnlyList<StaffAttendanceRecordResponse>>> ListStaffAttendanceAsync(
        string personType, DateTime date, CancellationToken ct = default)
    {
        var type = NormPersonType(personType);
        if (type is null)
            return ApiResult<IReadOnlyList<StaffAttendanceRecordResponse>>.Fail(
                new Error("invalid_request", "personType must be 'teacher' or 'staff'"), 400);
        if (type == "staff" && !FeatureGate.Allowed(tenant, features, FeatureCatalog.StaffSupport))
            return FeatureGate.Locked<IReadOnlyList<StaffAttendanceRecordResponse>>(FeatureCatalog.StaffSupport);
        return ApiResult<IReadOnlyList<StaffAttendanceRecordResponse>>.Ok(
            await staffAttendance.ListAsync(type, date, ct));
    }

    public async Task<ApiResult> BulkUpsertStaffAttendanceAsync(
        BulkStaffAttendanceRequest req, CancellationToken ct = default)
    {
        if (tenant.TenantId is not { } tid)
            return ApiResult.Fail(new Error("forbidden", "no tenant context"), 403);
        var type = NormPersonType(req.PersonType);
        if (type is null)
            return ApiResult.Fail(new Error("invalid_request", "personType must be 'teacher' or 'staff'"), 400);
        if (type == "staff" && !FeatureGate.Allowed(tenant, features, FeatureCatalog.StaffSupport))
            return FeatureGate.Locked(FeatureCatalog.StaffSupport);
        await staffAttendance.BulkUpsertAsync(tid, type, req.Date, tenant.UserId, req.Records, ct);
        return ApiResult.NoContent();
    }

    public async Task<ApiResult<IReadOnlyList<StaffAttendanceRecordResponse>>> ListStaffAttendanceForPersonAsync(
        string personType, Guid personId, DateTime from, DateTime to, CancellationToken ct = default)
    {
        var type = NormPersonType(personType);
        if (type is null)
            return ApiResult<IReadOnlyList<StaffAttendanceRecordResponse>>.Fail(
                new Error("invalid_request", "personType must be 'teacher' or 'staff'"), 400);
        if (type == "staff" && !FeatureGate.Allowed(tenant, features, FeatureCatalog.StaffSupport))
            return FeatureGate.Locked<IReadOnlyList<StaffAttendanceRecordResponse>>(FeatureCatalog.StaffSupport);
        return ApiResult<IReadOnlyList<StaffAttendanceRecordResponse>>.Ok(
            await staffAttendance.ListForPersonAsync(type, personId, from, to, ct));
    }

    public async Task<ApiResult<IReadOnlyList<ExamResponse>>> ListExamsAsync(CancellationToken ct = default) =>
        ApiResult<IReadOnlyList<ExamResponse>>.Ok(await exams.ListExamsAsync(ct));

    public async Task<ApiResult<ExamResponse>> GetExamAsync(Guid id, CancellationToken ct = default)
    {
        var e = await exams.GetExamAsync(id, ct);
        return e is null
            ? ApiResult<ExamResponse>.Fail(new Error("not_found", "resource not found"), 404)
            : ApiResult<ExamResponse>.Ok(e);
    }

    public async Task<ApiResult<ExamResponse>> CreateExamAsync(CreateExamRequest req, CancellationToken ct = default)
    {
        if (tenant.TenantId is not { } tid)
            return ApiResult<ExamResponse>.Fail(new Error("forbidden", "no tenant context"), 403);
        return ApiResult<ExamResponse>.Ok((await exams.CreateExamAsync(tid, req, ct))!, 201);
    }

    public async Task<ApiResult<ExamResponse>> UpdateExamAsync(Guid id, UpdateExamRequest req, CancellationToken ct = default)
    {
        if (await exams.GetExamAsync(id, ct) is null)
            return ApiResult<ExamResponse>.Fail(new Error("not_found", "resource not found"), 404);
        return ApiResult<ExamResponse>.Ok((await exams.UpdateExamAsync(id, req, ct))!);
    }

    public async Task<ApiResult<IReadOnlyList<ExamPaperResponse>>> ListExamPapersAsync(
        Guid? examId, CancellationToken ct = default) =>
        ApiResult<IReadOnlyList<ExamPaperResponse>>.Ok(await exams.ListExamPapersAsync(examId, ct));

    public async Task<ApiResult<ExamPaperResponse>> GetExamPaperAsync(Guid id, CancellationToken ct = default)
    {
        var p = await exams.GetExamPaperAsync(id, ct);
        return p is null
            ? ApiResult<ExamPaperResponse>.Fail(new Error("not_found", "resource not found"), 404)
            : ApiResult<ExamPaperResponse>.Ok(p);
    }

    public async Task<ApiResult<ExamPaperResponse>> CreateExamPaperAsync(
        CreateExamPaperRequest req, CancellationToken ct = default)
    {
        if (tenant.TenantId is not { } tid)
            return ApiResult<ExamPaperResponse>.Fail(new Error("forbidden", "no tenant context"), 403);
        var created = await exams.CreateExamPaperAsync(tid, req, ct);
        if (created is null)
            return ApiResult<ExamPaperResponse>.Fail(new Error("internal_error", "could not create exam paper"), 500);
        try
        {
            await academicsNotifier.NotifyClassTestScheduledAsync(
                tid, created.Name ?? req.Name, created.Subject ?? req.Subject, created.Date ?? req.Date, ct);
        }
        catch
        {
            /* notification is best-effort; paper create already succeeded */
        }
        return ApiResult<ExamPaperResponse>.Ok(created, 201);
    }

    public async Task<ApiResult<ExamPaperResponse>> UpdateExamPaperAsync(
        Guid id, UpdateExamPaperRequest req, CancellationToken ct = default)
    {
        var updated = await exams.UpdateExamPaperAsync(id, req, ct);
        return updated is null
            ? ApiResult<ExamPaperResponse>.Fail(new Error("not_found", "resource not found"), 404)
            : ApiResult<ExamPaperResponse>.Ok(updated);
    }

    public async Task<ApiResult> DeleteExamPaperAsync(Guid id, CancellationToken ct = default)
    {
        await exams.DeleteExamPaperAsync(id, ct);
        return ApiResult.NoContent();
    }

    public async Task<ApiResult<IReadOnlyList<GradeResponse>>> ListGradesAsync(
        Guid examPaperId, CancellationToken ct = default) =>
        ApiResult<IReadOnlyList<GradeResponse>>.Ok(await exams.ListGradesAsync(examPaperId, ct));

    public async Task<ApiResult<GradeResponse>> UpsertGradeAsync(UpsertGradeRequest req, CancellationToken ct = default)
    {
        if (tenant.TenantId is not { } tid)
            return ApiResult<GradeResponse>.Fail(new Error("forbidden", "no tenant context"), 403);
        return ApiResult<GradeResponse>.Ok((await exams.UpsertGradeAsync(tid, req, ct))!);
    }

    public async Task<ApiResult<IReadOnlyList<ExamAttendanceRecordResponse>>> ListExamAttendanceAsync(
        Guid examPaperId, CancellationToken ct = default) =>
        ApiResult<IReadOnlyList<ExamAttendanceRecordResponse>>.Ok(
            await exams.ListExamAttendanceAsync(examPaperId, ct));

    public async Task<ApiResult> BulkUpsertExamAttendanceAsync(
        Guid examPaperId, BulkExamAttendanceRequest req, CancellationToken ct = default)
    {
        if (tenant.TenantId is not { } tid)
            return ApiResult.Fail(new Error("forbidden", "no tenant context"), 403);
        if (await exams.GetExamPaperAsync(examPaperId, ct) is null)
            return ApiResult.Fail(new Error("not_found", "resource not found"), 404);
        await exams.BulkUpsertExamAttendanceAsync(tid, examPaperId, tenant.UserId, req.Records, ct);
        return ApiResult.NoContent();
    }

    public async Task<ApiResult<IReadOnlyList<HomeworkResponse>>> ListHomeworkAsync(
        Guid? studentId, string? status, CancellationToken ct = default) =>
        ApiResult<IReadOnlyList<HomeworkResponse>>.Ok(await homework.ListAsync(studentId, status, ct));

    public async Task<ApiResult<HomeworkResponse>> GetHomeworkAsync(Guid id, CancellationToken ct = default)
    {
        var h = await homework.GetAsync(id, ct);
        return h is null
            ? ApiResult<HomeworkResponse>.Fail(new Error("not_found", "resource not found"), 404)
            : ApiResult<HomeworkResponse>.Ok(h);
    }

    public async Task<ApiResult<HomeworkResponse>> CreateHomeworkAsync(
        CreateHomeworkRequest req, CancellationToken ct = default)
    {
        if (tenant.TenantId is not { } tid)
            return ApiResult<HomeworkResponse>.Fail(new Error("forbidden", "no tenant context"), 403);
        return ApiResult<HomeworkResponse>.Ok((await homework.CreateAsync(tid, req, ct))!, 201);
    }

    public async Task<ApiResult<HomeworkResponse>> SetHomeworkStatusAsync(
        Guid id, SetHomeworkStatusRequest req, CancellationToken ct = default)
    {
        if (await homework.GetAsync(id, ct) is null)
            return ApiResult<HomeworkResponse>.Fail(new Error("not_found", "resource not found"), 404);
        return ApiResult<HomeworkResponse>.Ok((await homework.SetStatusAsync(id, req.Status, ct))!);
    }

    public async Task<ApiResult<HomeworkResponse>> SubmitHomeworkAsync(Guid id, CancellationToken ct = default)
    {
        if (await homework.GetAsync(id, ct) is null)
            return ApiResult<HomeworkResponse>.Fail(new Error("not_found", "resource not found"), 404);
        return ApiResult<HomeworkResponse>.Ok((await homework.SetStatusAsync(id, "submitted", ct))!);
    }

    public async Task<ApiResult<IReadOnlyList<TimetableSlotResponse>>> ListTimetableAsync(
        ClaimsPrincipal caller, CancellationToken ct = default)
    {
        var roles = caller.FindAll("role").Select(c => c.Value).ToArray();
        var isPrincipal = roles.Any(r => r.Split('.').LastOrDefault() == "principal");
        if (isPrincipal)
            return ApiResult<IReadOnlyList<TimetableSlotResponse>>.Ok(await timetable.ListAsync(ct));

        var sub = caller.FindFirst("sub")?.Value;
        if (sub is null || !Guid.TryParse(sub, out var userId))
            return ApiResult<IReadOnlyList<TimetableSlotResponse>>.Fail(new Error("unauthorized", "unauthorized"), 401);
        return ApiResult<IReadOnlyList<TimetableSlotResponse>>.Ok(await timetable.ListForTeacherAsync(userId, ct));
    }

    public async Task<ApiResult<TimetableSlotResponse>> CreateTimetableSlotAsync(
        CreateTimetableSlotRequest req, CancellationToken ct = default)
    {
        if (tenant.TenantId is not { } tid)
            return ApiResult<TimetableSlotResponse>.Fail(new Error("forbidden", "no tenant context"), 403);
        return ApiResult<TimetableSlotResponse>.Ok((await timetable.CreateAsync(tid, req, ct))!, 201);
    }

    public async Task<ApiResult> DeleteTimetableSlotAsync(Guid id, CancellationToken ct = default)
    {
        if (tenant.TenantId is not { } tid)
            return ApiResult.Fail(new Error("forbidden", "no tenant context"), 403);
        if (await timetable.GetAsync(id, ct) is null)
            return ApiResult.Fail(new Error("not_found", "resource not found"), 404);
        await timetable.DeleteAsync(id, tid, ct);
        return ApiResult.NoContent();
    }

    public async Task<ApiResult<IReadOnlyList<CalendarEventResponse>>> ListCalendarEventsAsync(CancellationToken ct = default) =>
        ApiResult<IReadOnlyList<CalendarEventResponse>>.Ok(await calendar.ListAsync(ct));

    public async Task<ApiResult<CalendarEventResponse>> CreateCalendarEventAsync(
        CreateCalendarEventRequest req, CancellationToken ct = default)
    {
        if (tenant.TenantId is not { } tid)
            return ApiResult<CalendarEventResponse>.Fail(new Error("forbidden", "no tenant context"), 403);
        return ApiResult<CalendarEventResponse>.Ok((await calendar.CreateAsync(tid, req, ct))!, 201);
    }

    public async Task<ApiResult<IReadOnlyList<LibraryBookResponse>>> ListLibraryBooksAsync(CancellationToken ct = default)
    {
        if (!FeatureGate.Allowed(tenant, features, FeatureCatalog.Operations))
            return FeatureGate.Locked<IReadOnlyList<LibraryBookResponse>>(FeatureCatalog.Operations);
        return ApiResult<IReadOnlyList<LibraryBookResponse>>.Ok(await library.ListAsync(clock.UtcNow, ct));
    }

    /// Flat late-fee rate (₹/day) applied to overdue books when computing library fines due.
    private const decimal LibraryFinePerDay = 5m;

    public async Task<ApiResult<LibrarySummaryResponse>> GetLibrarySummaryAsync(CancellationToken ct = default)
    {
        if (!FeatureGate.Allowed(tenant, features, FeatureCatalog.Operations))
            return FeatureGate.Locked<LibrarySummaryResponse>(FeatureCatalog.Operations);
        return ApiResult<LibrarySummaryResponse>.Ok(await library.SummaryAsync(clock.UtcNow, LibraryFinePerDay, ct));
    }

    public async Task<ApiResult<LibraryBookResponse>> CreateLibraryBookAsync(
        CreateLibraryBookRequest req, CancellationToken ct = default)
    {
        if (!FeatureGate.Allowed(tenant, features, FeatureCatalog.Operations))
            return FeatureGate.Locked<LibraryBookResponse>(FeatureCatalog.Operations);
        if (tenant.TenantId is not { } tid)
            return ApiResult<LibraryBookResponse>.Fail(new Error("forbidden", "no tenant context"), 403);
        return ApiResult<LibraryBookResponse>.Ok((await library.CreateAsync(tid, req, ct))!, 201);
    }

    public async Task<ApiResult<IReadOnlyList<AssignmentResponse>>> ListAssignmentsAsync(CancellationToken ct = default) =>
        ApiResult<IReadOnlyList<AssignmentResponse>>.Ok(await assignments.ListAsync(clock.UtcNow, ct));

    public async Task<ApiResult<AssignmentResponse>> CreateAssignmentAsync(
        CreateAssignmentRequest req, CancellationToken ct = default)
    {
        if (tenant.TenantId is not { } tid)
            return ApiResult<AssignmentResponse>.Fail(new Error("forbidden", "no tenant context"), 403);
        var created = await assignments.CreateAsync(tid, req, ct);
        if (created is null)
            return ApiResult<AssignmentResponse>.Fail(new Error("internal_error", "could not create assignment"), 500);
        try
        {
            await academicsNotifier.NotifyHomeworkAssignedAsync(
                tid, req.Title, created.ClassName ?? req.ClassName, created.DueDate ?? req.DueDate, ct);
        }
        catch
        {
            /* best-effort */
        }
        return ApiResult<AssignmentResponse>.Ok(created, 201);
    }
}
