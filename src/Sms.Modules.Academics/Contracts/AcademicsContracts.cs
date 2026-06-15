namespace Sms.Modules.Academics.Contracts;

// ---- Class ----
public sealed record ClassResponse(
    Guid Id, Guid TenantId, string Name, string? Grade, string? Section, string? Subject,
    string? Room, int StudentCount, Guid? ClassTeacherId);
public sealed record CreateClassRequest(
    string Name, string? Grade, string? Section, string? Subject, string? Room, Guid? ClassTeacherId);

// ---- Subject ----
public sealed record SubjectResponse(Guid Id, Guid TenantId, string Name, string? Short, Guid? TeacherId, string? Color);
public sealed record CreateSubjectRequest(string Name, string? Short, Guid? TeacherId, string? Color);

// ---- Attendance (roll-call) ----
public sealed record AttendanceRecordResponse(
    Guid Id, Guid TenantId, Guid ClassId, Guid StudentId, DateTime Date, string Status, Guid? MarkedBy);
public sealed record AttendanceUpsertRow(Guid StudentId, string Status);
public sealed record BulkAttendanceRequest(DateTime Date, IReadOnlyList<AttendanceUpsertRow> Records);
