using System.Data;
using Dapper;
using Sms.Modules.Academics.Contracts;
using Sms.Shared.Kernel.Data;

namespace Sms.Modules.Academics.Data;

public sealed class ClassRepository(IDbConnectionFactory factory) : BaseRepository(factory)
{
    private const string Cols = "Id, TenantId, Name, Grade, Section, Subject, Room, StudentCount, ClassTeacherId";

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
        (await QueryInlineAsync<ClassResponse>($"SELECT {Cols} FROM dbo.Classes WHERE Id = @id", new { id }, ct))
        .FirstOrDefault();

    public Task<IReadOnlyList<ClassResponse>> ListAsync(CancellationToken ct = default) =>
        QueryInlineAsync<ClassResponse>($"SELECT {Cols} FROM dbo.Classes ORDER BY Name", null, ct);
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
}
