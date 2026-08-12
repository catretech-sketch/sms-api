using Sms.Modules.Academics.Contracts;
using Sms.Shared.Kernel.Data;

namespace Sms.Modules.Academics.Data;

public sealed class TimetableRepository(IDbConnectionFactory factory) : BaseRepository(factory)
{
    private const string Cols = "Id, TenantId, [Day], Period, Subject, ClassId, ClassName, Room, StartTime, EndTime";
    private const string TeacherCols =
        "ts.Id, ts.TenantId, ts.[Day], ts.Period, ts.Subject, ts.ClassId, ts.ClassName, ts.Room, ts.StartTime, ts.EndTime";

    // Teacher assignment is per-slot (sms-admin lets different periods of the same subject
    // have different teachers to resolve clashes), so TeacherName resolves via ts.TeacherId
    // directly. Subjects.TeacherId is only a fallback for slots published before TeacherId
    // existed (still per-subject-default, so it can be wrong when a class later reassigns
    // a specific period to a different teacher than the subject's default).
    public Task<IReadOnlyList<TimetableSlotResponse>> ListAsync(CancellationToken ct = default) =>
        QueryInlineAsync<TimetableSlotResponse>($@"
SELECT {TeacherCols}, COALESCE(t1.Name, t2.Name) AS TeacherName
FROM dbo.TimetableSlots ts
LEFT JOIN dbo.Teachers t1 ON t1.Id = ts.TeacherId
LEFT JOIN dbo.Subjects sub ON sub.Name = ts.Subject
LEFT JOIN dbo.Teachers t2 ON t2.Id = sub.TeacherId
ORDER BY ts.[Day], ts.Period", null, ct);

    /// Slots derivable as "this teacher's own": either they're the linked class-teacher
    /// for the slot's class, they're the slot's directly-assigned TeacherId, or they're the
    /// assigned teacher for a subject whose name matches the slot's free-text Subject (legacy
    /// fallback for slots published before TeacherId existed). A slot with none of these
    /// linkages won't appear for anyone — known limitation, strictly narrower than the prior
    /// whole-tenant leak.
    public Task<IReadOnlyList<TimetableSlotResponse>> ListForTeacherAsync(Guid teacherUserId, CancellationToken ct = default) =>
        QueryInlineAsync<TimetableSlotResponse>($@"
SELECT {TeacherCols}, COALESCE(t1.Name, t2.Name) AS TeacherName
FROM dbo.TimetableSlots ts
JOIN dbo.Teachers t ON t.UserId = @teacherUserId
LEFT JOIN dbo.Classes c ON c.Id = ts.ClassId
LEFT JOIN dbo.Subjects sub ON sub.Name = ts.Subject
LEFT JOIN dbo.Teachers t1 ON t1.Id = ts.TeacherId
LEFT JOIN dbo.Teachers t2 ON t2.Id = sub.TeacherId
WHERE c.ClassTeacherId = t.Id OR ts.TeacherId = t.Id OR sub.TeacherId = t.Id
ORDER BY ts.[Day], ts.Period", new { teacherUserId }, ct);

    public Task<IReadOnlyList<ClassDaySlotRow>> ListForClassDayAsync(
        Guid classId, string day, CancellationToken ct = default) =>
        QueryInlineAsync<ClassDaySlotRow>(@"
SELECT ts.Period, ts.Subject,
       COALESCE(ts.TeacherId, t2.Id) AS TeacherId,
       COALESCE(t1.Name, t2.Name) AS TeacherName,
       ts.StartTime, ts.EndTime
FROM dbo.TimetableSlots ts
LEFT JOIN dbo.Teachers t1 ON t1.Id = ts.TeacherId
LEFT JOIN dbo.Subjects sub ON sub.Name = ts.Subject
LEFT JOIN dbo.Teachers t2 ON t2.Id = sub.TeacherId
WHERE ts.ClassId = @classId AND ts.[Day] = @day
ORDER BY ts.Period", new { classId, day }, ct);

    public Task<TimetableSlotResponse?> CreateAsync(Guid tenantId, CreateTimetableSlotRequest r, CancellationToken ct = default) =>
        QuerySingleProcAsync<TimetableSlotResponse>("dbo.TimetableSlot_Create", new
        {
            TenantId = tenantId, r.Day, r.Period, r.Subject, r.ClassId, r.ClassName, r.Room, r.StartTime, r.EndTime,
            r.TeacherId,
        }, ct);

    public async Task<TimetableSlotResponse?> GetAsync(Guid id, CancellationToken ct = default) =>
        (await QueryInlineAsync<TimetableSlotResponse>(
            $"SELECT {Cols} FROM dbo.TimetableSlots WHERE Id = @id", new { id }, ct))
        .FirstOrDefault();

    public Task<int> DeleteAsync(Guid id, Guid tenantId, CancellationToken ct = default) =>
        ExecuteProcAsync("dbo.TimetableSlot_Delete", new { Id = id, TenantId = tenantId }, ct);
}
