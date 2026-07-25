namespace Sms.Modules.Staffing.Contracts;

// Dapper materializes via a constructor matching the EXACT column count of whatever query
// ran — it won't mix a matched-prefix constructor with leftover property setters. Priority
// is now a real, always-present column (default 'medium'), so it's baked into the primary
// 13-param constructor - GetAsync/ListMineAsync/Leave_Create/Leave_Decide all consistently
// return 13 columns. RequesterName is JOIN-derived and only present for the approvals query
// (ListByStatusAsync, 14 columns), so it stays a trailing init-only property with its own
// secondary constructor, same reasoning as Task 6.
public sealed record LeaveResponse(
    Guid Id, Guid TenantId, Guid? RequesterId, Guid? ChildId, string Type, DateTime? FromDate, DateTime? ToDate,
    string? Reason, string? Substitute, string Status, DateTime? AppliedOn, string? DecidedNote, string Priority)
{
    public string? RequesterName { get; init; }

    public LeaveResponse(
        Guid Id, Guid TenantId, Guid? RequesterId, Guid? ChildId, string Type, DateTime? FromDate, DateTime? ToDate,
        string? Reason, string? Substitute, string Status, DateTime? AppliedOn, string? DecidedNote, string Priority,
        string? RequesterName)
        : this(Id, TenantId, RequesterId, ChildId, Type, FromDate, ToDate, Reason, Substitute, Status, AppliedOn, DecidedNote, Priority) =>
        this.RequesterName = RequesterName;
}

public sealed record CreateLeaveRequest(
    string Type, DateTime? FromDate, DateTime? ToDate, string? Reason, string? Substitute, Guid? ChildId,
    string? Priority = null);

public sealed record DecideLeaveRequest(string Status, string? DecidedNote);
