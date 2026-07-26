using System.Data;
using Dapper;
using Sms.Modules.Academics.Contracts;
using Sms.Shared.Kernel.Data;

namespace Sms.Modules.Academics.Data;

public sealed class ClassRepository(IDbConnectionFactory factory) : BaseRepository(factory)
{
    private const string Cols = "Id, TenantId, Name, Grade, Section, Subject, Room, StudentCount, ClassTeacherId";

    // Live count via OUTER APPLY, falling back to the stored (stubbed) column only if the
    // fallback-count query itself returns NULL (no matching Students rows at all, vs. a
    // genuine zero). Matches Students to a class the same way ReportingRepository does:
    // Grade+Section when both are set, else the free-text ClassLabel/Name. Also derives
    // NextPeriod from the next upcoming TimetableSlot for the class today.
    //
    // Known limitation: StartTime is compared as an "HH:mm" string against a UTC-formatted
    // current time (matching TimetableSlots.StartTime's existing nvarchar(10) shape) - if
    // the school's local timezone differs from UTC, "next period" could be off. Timezone
    // handling for timetables is a pre-existing condition across this module, not introduced
    // by this fix.
    private const string ClassSelectWithLiveCountAndNextPeriod = @"
SELECT c.Id, c.TenantId, c.Name, c.Grade, c.Section, c.Subject, c.Room,
       CASE WHEN sc.Cnt IS NOT NULL THEN sc.Cnt ELSE c.StudentCount END AS StudentCount,
       c.ClassTeacherId, np.Subject AS NextPeriod
FROM dbo.Classes c
OUTER APPLY (
    SELECT COUNT(*) AS Cnt FROM dbo.Students s
    WHERE s.Status = N'active'
      AND (
        (c.Grade IS NOT NULL AND c.Section IS NOT NULL AND s.Grade = c.Grade AND s.Section = c.Section)
        OR (c.Name IS NOT NULL AND s.ClassLabel = c.Name)
      )
) sc
OUTER APPLY (
    SELECT TOP 1 ts.Subject
    FROM dbo.TimetableSlots ts
    WHERE ts.ClassId = c.Id
      AND ts.[Day] = LEFT(DATENAME(WEEKDAY, GETUTCDATE()), 3)
      AND ts.StartTime > FORMAT(GETUTCDATE(), 'HH:mm')
    ORDER BY ts.Period
) np";

    public Task<ClassResponse?> CreateAsync(Guid tenantId, CreateClassRequest r, CancellationToken ct = default) =>
        QuerySingleProcAsync<ClassResponse>("dbo.Class_Create",
            new { TenantId = tenantId, r.Name, r.Grade, r.Section, r.Subject, r.Room, r.ClassTeacherId }, ct);

    public Task<ClassResponse?> UpdateAsync(Guid id, Guid tenantId, UpdateClassRequest r, CancellationToken ct = default) =>
        QuerySingleProcAsync<ClassResponse>("dbo.Class_Update",
            new
            {
                Id = id,
                TenantId = tenantId,
                r.Name,
                r.Grade,
                r.Section,
                r.Subject,
                r.Room,
                r.ClassTeacherId,
                r.ClearClassTeacher,
            }, ct);

    public async Task<ClassResponse?> GetAsync(Guid id, CancellationToken ct = default) =>
        (await QueryInlineAsync<ClassResponse>($"{ClassSelectWithLiveCountAndNextPeriod} WHERE c.Id = @id", new { id }, ct))
        .FirstOrDefault();

    public Task<IReadOnlyList<ClassResponse>> ListAsync(CancellationToken ct = default) =>
        QueryInlineAsync<ClassResponse>($"{ClassSelectWithLiveCountAndNextPeriod} ORDER BY c.Name", null, ct);
}

public sealed class SubjectRepository(IDbConnectionFactory factory) : BaseRepository(factory)
{
    private const string Cols = "Id, TenantId, Name, Short, TeacherId, Color";

    public Task<SubjectResponse?> CreateAsync(Guid tenantId, CreateSubjectRequest r, CancellationToken ct = default) =>
        QuerySingleProcAsync<SubjectResponse>("dbo.Subject_Create",
            new { TenantId = tenantId, r.Name, r.Short, r.TeacherId, r.Color }, ct);

    public Task<SubjectResponse?> UpdateAsync(Guid id, Guid tenantId, UpdateSubjectRequest r, CancellationToken ct = default) =>
        QuerySingleProcAsync<SubjectResponse>("dbo.Subject_Update",
            new
            {
                Id = id,
                TenantId = tenantId,
                r.Name,
                r.Short,
                r.TeacherId,
                r.Color,
                r.ClearTeacher,
            }, ct);

    public async Task<SubjectResponse?> GetAsync(Guid id, CancellationToken ct = default) =>
        (await QueryInlineAsync<SubjectResponse>($"SELECT {Cols} FROM dbo.Subjects WHERE Id = @id", new { id }, ct))
        .FirstOrDefault();

    public Task<IReadOnlyList<SubjectResponse>> ListAsync(CancellationToken ct = default) =>
        QueryInlineAsync<SubjectResponse>($"SELECT {Cols} FROM dbo.Subjects ORDER BY Name", null, ct);

    public Task<int> DeleteAsync(Guid id, Guid tenantId, CancellationToken ct = default) =>
        ExecuteProcAsync("dbo.Subject_Delete", new { Id = id, TenantId = tenantId }, ct);
}

public sealed class AttendanceRepository(IDbConnectionFactory factory) : BaseRepository(factory)
{
    /// Bulk roll-call upsert via a table-valued parameter (one set-based round-trip).
    public Task BulkUpsertAsync(Guid tenantId, Guid classId, DateTime date, Guid? markedBy,
        IReadOnlyList<AttendanceUpsertRow> rows, CancellationToken ct = default)
    {
        var table = new DataTable();
        table.Columns.Add("StudentId", typeof(Guid));
        table.Columns.Add("Status", typeof(string));
        foreach (var r in rows) table.Rows.Add(r.StudentId, r.Status);

        var p = new DynamicParameters();
        p.Add("@TenantId", tenantId);
        p.Add("@ClassId", classId);
        p.Add("@Date", date.Date);
        p.Add("@MarkedBy", markedBy);
        p.Add("@Rows", table.AsTableValuedParameter("dbo.AttendanceTvp"));
        return ExecuteProcAsync("dbo.Attendance_BulkUpsert", p, ct);
    }

    public Task<IReadOnlyList<AttendanceRecordResponse>> ListAsync(
        Guid classId, DateTime date, CancellationToken ct = default) =>
        QueryInlineAsync<AttendanceRecordResponse>(
            "SELECT Id, TenantId, ClassId, StudentId, [Date], Status, MarkedBy FROM dbo.AttendanceRecords " +
            "WHERE ClassId = @classId AND [Date] = @date ORDER BY StudentId",
            new { classId, date = date.Date }, ct);

    /// Every day-mark for the current tenant since a date (RLS-scoped). Used to compute absence streaks.
    public Task<IReadOnlyList<AttendanceMarkRow>> ListSinceAsync(DateTime since, CancellationToken ct = default) =>
        QueryInlineAsync<AttendanceMarkRow>(
            "SELECT StudentId, [Date], Status FROM dbo.AttendanceRecords " +
            "WHERE [Date] >= @since ORDER BY StudentId, [Date]",
            new { since = since.Date }, ct);

    public Task<IReadOnlyList<AttendanceRecordResponse>> ListForStudentAsync(
        Guid studentId, DateTime from, DateTime to, CancellationToken ct = default) =>
        QueryInlineAsync<AttendanceRecordResponse>(
            "SELECT Id, TenantId, ClassId, StudentId, [Date], Status, MarkedBy FROM dbo.AttendanceRecords " +
            "WHERE StudentId = @studentId AND [Date] BETWEEN @from AND @to ORDER BY [Date]",
            new { studentId, from = from.Date, to = to.Date }, ct);
}

/// Admin/principal marking a teacher or staff member's daily attendance — distinct from
/// Sms.Modules.Attendance, which is self check-in/out punches for the logged-in user.
public sealed class StaffAttendanceRepository(IDbConnectionFactory factory) : BaseRepository(factory)
{
    public Task BulkUpsertAsync(Guid tenantId, string personType, DateTime date, Guid? markedBy,
        IReadOnlyList<StaffAttendanceUpsertRow> rows, CancellationToken ct = default)
    {
        var table = new DataTable();
        table.Columns.Add("PersonId", typeof(Guid));
        table.Columns.Add("Status", typeof(string));
        foreach (var r in rows) table.Rows.Add(r.PersonId, r.Status);

        var p = new DynamicParameters();
        p.Add("@TenantId", tenantId);
        p.Add("@PersonType", personType);
        p.Add("@Date", date.Date);
        p.Add("@MarkedBy", markedBy);
        p.Add("@Rows", table.AsTableValuedParameter("dbo.StaffAttendanceTvp"));
        return ExecuteProcAsync("dbo.StaffAttendance_BulkUpsert", p, ct);
    }

    public Task<IReadOnlyList<StaffAttendanceRecordResponse>> ListAsync(
        string personType, DateTime date, CancellationToken ct = default) =>
        QueryInlineAsync<StaffAttendanceRecordResponse>(
            "SELECT Id, TenantId, PersonType, PersonId, [Date], Status, MarkedBy FROM dbo.StaffAttendanceRecords " +
            "WHERE PersonType = @personType AND [Date] = @date ORDER BY PersonId",
            new { personType, date = date.Date }, ct);

    public Task<IReadOnlyList<StaffAttendanceRecordResponse>> ListForPersonAsync(
        string personType, Guid personId, DateTime from, DateTime to, CancellationToken ct = default) =>
        QueryInlineAsync<StaffAttendanceRecordResponse>(
            "SELECT Id, TenantId, PersonType, PersonId, [Date], Status, MarkedBy FROM dbo.StaffAttendanceRecords " +
            "WHERE PersonType = @personType AND PersonId = @personId AND [Date] BETWEEN @from AND @to ORDER BY [Date]",
            new { personType, personId, from = from.Date, to = to.Date }, ct);
}
