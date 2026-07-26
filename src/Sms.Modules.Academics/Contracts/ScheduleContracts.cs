namespace Sms.Modules.Academics.Contracts;

// TeacherName is a trailing init-only property with a secondary constructor, not a primary-
// constructor parameter: Dapper materializes via a constructor matching the exact column count
// of whatever query ran, and TimetableSlot_Create/GetAsync still return the original 10 columns
// while the principal-facing ListAsync now returns 11 (+ TeacherName).
public sealed record TimetableSlotResponse(
    Guid Id, Guid TenantId, string Day, int Period, string? Subject, Guid? ClassId, string? ClassName,
    string? Room, string? StartTime, string? EndTime)
{
    public string? TeacherName { get; init; }

    public TimetableSlotResponse(
        Guid Id, Guid TenantId, string Day, int Period, string? Subject, Guid? ClassId, string? ClassName,
        string? Room, string? StartTime, string? EndTime, string? TeacherName)
        : this(Id, TenantId, Day, Period, Subject, ClassId, ClassName, Room, StartTime, EndTime) =>
        this.TeacherName = TeacherName;
}
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

/// Aggregate KPIs for the Operations · Library dashboard. All values derived from LibraryBooks.
public sealed record LibrarySummaryResponse(int Catalogue, int Members, int Issued, decimal FinesDue);
