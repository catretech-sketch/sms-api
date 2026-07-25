namespace Sms.Modules.Staffing.Contracts;

// Dapper materializes via a constructor matching the EXACT column count of whatever query
// ran — it won't mix a matched-prefix constructor with leftover property setters. Since
// Leave_Create/Leave_Decide/GetAsync/ListMineAsync all still return the original 12
// columns while ListByStatusAsync now returns 13 (+ RequesterName), we need two
// constructors: the 12-param primary (unchanged callers) and a 13-param overload Dapper
// picks up for the approvals query.
public sealed record LeaveResponse(
    Guid Id, Guid TenantId, Guid? RequesterId, Guid? ChildId, string Type, DateTime? FromDate, DateTime? ToDate,
    string? Reason, string? Substitute, string Status, DateTime? AppliedOn, string? DecidedNote)
{
    public string? RequesterName { get; init; }

    public LeaveResponse(
        Guid Id, Guid TenantId, Guid? RequesterId, Guid? ChildId, string Type, DateTime? FromDate, DateTime? ToDate,
        string? Reason, string? Substitute, string Status, DateTime? AppliedOn, string? DecidedNote, string? RequesterName)
        : this(Id, TenantId, RequesterId, ChildId, Type, FromDate, ToDate, Reason, Substitute, Status, AppliedOn, DecidedNote) =>
        this.RequesterName = RequesterName;
}

public sealed record CreateLeaveRequest(
    string Type, DateTime? FromDate, DateTime? ToDate, string? Reason, string? Substitute, Guid? ChildId);

public sealed record DecideLeaveRequest(string Status, string? DecidedNote);
