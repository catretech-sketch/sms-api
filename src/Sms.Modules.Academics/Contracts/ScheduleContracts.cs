namespace Sms.Modules.Academics.Contracts;

public sealed record TimetableSlotResponse(
    Guid Id, Guid TenantId, string Day, int Period, string? Subject, Guid? ClassId, string? ClassName,
    string? Room, string? StartTime, string? EndTime);
public sealed record CreateTimetableSlotRequest(
    string Day, int Period, string? Subject, Guid? ClassId, string? ClassName, string? Room,
    string? StartTime, string? EndTime);
