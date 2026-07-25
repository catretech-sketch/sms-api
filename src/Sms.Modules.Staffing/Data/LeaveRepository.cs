using Sms.Modules.Staffing.Contracts;
using Sms.Shared.Kernel.Data;

namespace Sms.Modules.Staffing.Data;

public sealed class LeaveRepository(IDbConnectionFactory factory) : BaseRepository(factory)
{
    private const string Cols =
        "Id, TenantId, RequesterId, ChildId, Type, FromDate, ToDate, Reason, Substitute, Status, AppliedOn, DecidedNote, Priority";

    public Task<LeaveResponse?> CreateAsync(Guid tenantId, Guid? requesterId, CreateLeaveRequest r, CancellationToken ct = default) =>
        QuerySingleProcAsync<LeaveResponse>("dbo.Leave_Create", new
        {
            TenantId = tenantId, RequesterId = requesterId, r.ChildId, r.Type, r.FromDate, r.ToDate, r.Reason, r.Substitute,
            Priority = r.Priority ?? "medium"
        }, ct);

    public Task<LeaveResponse?> DecideAsync(Guid id, string status, Guid? decidedBy, string? note, CancellationToken ct = default) =>
        QuerySingleProcAsync<LeaveResponse>("dbo.Leave_Decide",
            new { Id = id, Status = status, DecidedBy = decidedBy, DecidedNote = note }, ct);

    public async Task<LeaveResponse?> GetAsync(Guid id, CancellationToken ct = default) =>
        (await QueryInlineAsync<LeaveResponse>($"SELECT {Cols} FROM dbo.LeaveRequests WHERE Id = @id", new { id }, ct))
        .FirstOrDefault();

    public Task<IReadOnlyList<LeaveResponse>> ListMineAsync(Guid? requesterId, CancellationToken ct = default) =>
        QueryInlineAsync<LeaveResponse>(
            $"SELECT {Cols} FROM dbo.LeaveRequests WHERE RequesterId = @requesterId ORDER BY AppliedOn DESC",
            new { requesterId }, ct);

    public Task<IReadOnlyList<LeaveResponse>> ListByStatusAsync(string status, CancellationToken ct = default) =>
        QueryInlineAsync<LeaveResponse>(@"
SELECT lr.Id, lr.TenantId, lr.RequesterId, lr.ChildId, lr.Type, lr.FromDate, lr.ToDate,
       lr.Reason, lr.Substitute, lr.Status, lr.AppliedOn, lr.DecidedNote, lr.Priority, u.Name AS RequesterName
FROM dbo.LeaveRequests lr
LEFT JOIN dbo.Users u ON u.Id = lr.RequesterId
WHERE lr.Status = @status
ORDER BY lr.AppliedOn DESC", new { status }, ct);
}
