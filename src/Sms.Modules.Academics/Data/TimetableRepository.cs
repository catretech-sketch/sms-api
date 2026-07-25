using Sms.Modules.Academics.Contracts;
using Sms.Shared.Kernel.Data;

namespace Sms.Modules.Academics.Data;

public sealed class TimetableRepository(IDbConnectionFactory factory) : BaseRepository(factory)
{
    private const string Cols = "Id, TenantId, [Day], Period, Subject, ClassId, ClassName, Room, StartTime, EndTime";
    private const string TeacherCols =
        "ts.Id, ts.TenantId, ts.[Day], ts.Period, ts.Subject, ts.ClassId, ts.ClassName, ts.Room, ts.StartTime, ts.EndTime";

    public Task<IReadOnlyList<TimetableSlotResponse>> ListAsync(CancellationToken ct = default) =>
        QueryInlineAsync<TimetableSlotResponse>(
            $"SELECT {Cols} FROM dbo.TimetableSlots ORDER BY [Day], Period", null, ct);

    /// Slots derivable as "this teacher's own": either they're the linked class-teacher
    /// for the slot's class, or they're the assigned teacher for a subject whose name
    /// matches the slot's free-text Subject. A slot with neither linkage won't appear
    /// for anyone — known limitation, strictly narrower than the prior whole-tenant leak.
    public Task<IReadOnlyList<TimetableSlotResponse>> ListForTeacherAsync(Guid teacherUserId, CancellationToken ct = default) =>
        QueryInlineAsync<TimetableSlotResponse>($@"
SELECT {TeacherCols}
FROM dbo.TimetableSlots ts
JOIN dbo.Teachers t ON t.UserId = @teacherUserId
LEFT JOIN dbo.Classes c ON c.Id = ts.ClassId
LEFT JOIN dbo.Subjects sub ON sub.Name = ts.Subject
WHERE c.ClassTeacherId = t.Id OR sub.TeacherId = t.Id
ORDER BY ts.[Day], ts.Period", new { teacherUserId }, ct);

    public Task<TimetableSlotResponse?> CreateAsync(Guid tenantId, CreateTimetableSlotRequest r, CancellationToken ct = default) =>
        QuerySingleProcAsync<TimetableSlotResponse>("dbo.TimetableSlot_Create", new
        {
            TenantId = tenantId, r.Day, r.Period, r.Subject, r.ClassId, r.ClassName, r.Room, r.StartTime, r.EndTime
        }, ct);
}
