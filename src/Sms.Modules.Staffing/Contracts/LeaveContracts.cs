namespace Sms.Modules.Staffing.Contracts;

public sealed record LeaveResponse(
    Guid Id, Guid TenantId, Guid? RequesterId, Guid? ChildId, string Type, DateTime? FromDate, DateTime? ToDate,
    string? Reason, string? Substitute, string Status, DateTime? AppliedOn, string? DecidedNote);

public sealed record CreateLeaveRequest(
    string Type, DateTime? FromDate, DateTime? ToDate, string? Reason, string? Substitute, Guid? ChildId);

public sealed record DecideLeaveRequest(string Status, string? DecidedNote);
