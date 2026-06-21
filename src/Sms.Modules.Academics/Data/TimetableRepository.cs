using Sms.Modules.Academics.Contracts;
using Sms.Shared.Kernel.Data;

namespace Sms.Modules.Academics.Data;

public sealed class TimetableRepository(IDbConnectionFactory factory) : BaseRepository(factory)
{
    private const string Cols = "Id, TenantId, [Day], Period, Subject, ClassId, ClassName, Room, StartTime, EndTime";

    public Task<IReadOnlyList<TimetableSlotResponse>> ListAsync(CancellationToken ct = default) =>
        QueryInlineAsync<TimetableSlotResponse>(
            $"SELECT {Cols} FROM dbo.TimetableSlots ORDER BY [Day], Period", null, ct);

    public Task<TimetableSlotResponse?> CreateAsync(Guid tenantId, CreateTimetableSlotRequest r, CancellationToken ct = default) =>
        QuerySingleProcAsync<TimetableSlotResponse>("dbo.TimetableSlot_Create", new
        {
            TenantId = tenantId, r.Day, r.Period, r.Subject, r.ClassId, r.ClassName, r.Room, r.StartTime, r.EndTime
        }, ct);
}
