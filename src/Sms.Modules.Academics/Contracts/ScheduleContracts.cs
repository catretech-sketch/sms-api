namespace Sms.Modules.Academics.Contracts;

public sealed record TimetableSlotResponse(
    Guid Id, Guid TenantId, string Day, int Period, string? Subject, Guid? ClassId, string? ClassName,
    string? Room, string? StartTime, string? EndTime);
public sealed record CreateTimetableSlotRequest(
    string Day, int Period, string? Subject, Guid? ClassId, string? ClassName, string? Room,
    string? StartTime, string? EndTime);

public sealed record CalendarEventResponse(
    Guid Id, Guid TenantId, string Title, DateTime Date, string? Time, string Type, string? Description);
public sealed record CreateCalendarEventRequest(
    string Title, DateTime Date, string? Time, string Type, string? Description);

public sealed record LibraryBookResponse(
    Guid Id, Guid TenantId, string Title, string Author, string? Subject,
    string? IssuedTo, DateTime? DueDate, string Status);
public sealed record CreateLibraryBookRequest(
    string Title, string Author, string? Subject, string? IssuedTo, DateTime? DueDate, string? Status);
