namespace Sms.Modules.Academics.Contracts;



/// Timetable slot DTO. Property bag (not a positional record) so Dapper can map

/// both the 10-column Create/Get shape and the 12-column List shape (TeacherId + TeacherName)

/// by column name without constructor signature mismatches.

public sealed class TimetableSlotResponse

{

    public Guid Id { get; init; }

    public Guid TenantId { get; init; }

    public string Day { get; init; } = "";

    public int Period { get; init; }

    public string? Subject { get; init; }

    public Guid? ClassId { get; init; }

    public string? ClassName { get; init; }

    public string? Room { get; init; }

    public string? StartTime { get; init; }

    public string? EndTime { get; init; }

    public Guid? TeacherId { get; init; }

    public string? TeacherName { get; init; }

}



public sealed record CreateTimetableSlotRequest(

    string Day, int Period, string? Subject, Guid? ClassId, string? ClassName, string? Room,

    string? StartTime, string? EndTime, Guid? TeacherId = null);



/// Atomically replace all slots for the given classes (delete owned, then insert).

/// Used by admin publish so one HTTP call replaces hundreds of create/delete round-trips.

public sealed record ReplaceTimetableRequest(

    IReadOnlyList<Guid> ClassIds,

    IReadOnlyList<CreateTimetableSlotRequest> Slots);



/// One published slot for a class on one weekday, with teacher resolved for roll-call.

public sealed record ClassDaySlotRow(

    Guid Id, int Period, string? Subject, Guid? SubjectId, Guid? TeacherId, string? TeacherName,

    string? StartTime, string? EndTime);



public sealed record CalendarEventResponse(

    Guid Id, Guid TenantId, string Title, DateTime Date, string? Time, string Type, string? Description,

    string? ChannelsJson = null);

public sealed record CreateCalendarEventRequest(

    string Title, DateTime Date, string? Time, string Type, string? Description, string? ChannelsJson = null);



public sealed record LibraryBookResponse(

    Guid Id, Guid TenantId, string Title, string Author, string? Subject,

    string? IssuedTo, DateTime? DueDate, string Status);

public sealed record CreateLibraryBookRequest(

    string Title, string Author, string? Subject, string? IssuedTo, DateTime? DueDate, string? Status);



/// Aggregate KPIs for the Operations · Library dashboard. All values derived from LibraryBooks.

public sealed record LibrarySummaryResponse(int Catalogue, int Members, int Issued, decimal FinesDue);


