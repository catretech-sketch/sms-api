namespace Sms.Modules.Academics.Contracts;

// ---- Class ----
// NextPeriod is a trailing init-only property with a secondary 10-param constructor, not a
// primary-constructor parameter: Dapper materializes via a constructor matching the exact
// column count of whatever query ran, and Class_Create/Class_Update still return the
// original 9 columns while ListAsync/GetAsync now return 10 (+ NextPeriod).
public sealed record ClassResponse(
    Guid Id, Guid TenantId, string Name, string? Grade, string? Section, string? Subject,
    string? Room, int StudentCount, Guid? ClassTeacherId)
{
    public string? NextPeriod { get; init; }

    public ClassResponse(
        Guid Id, Guid TenantId, string Name, string? Grade, string? Section, string? Subject,
        string? Room, int StudentCount, Guid? ClassTeacherId, string? NextPeriod)
        : this(Id, TenantId, Name, Grade, Section, Subject, Room, StudentCount, ClassTeacherId) =>
        this.NextPeriod = NextPeriod;
}
public sealed record CreateClassRequest(
    string Name, string? Grade, string? Section, string? Subject, string? Room, Guid? ClassTeacherId);

public sealed record UpdateClassRequest(
    string? Name = null,
    string? Grade = null,
    string? Section = null,
    string? Subject = null,
    string? Room = null,
    Guid? ClassTeacherId = null,
    bool ClearClassTeacher = false);

// ---- Subject ----
public sealed record SubjectResponse(Guid Id, Guid TenantId, string Name, string? Short, Guid? TeacherId, string? Color);
public sealed record CreateSubjectRequest(string Name, string? Short, Guid? TeacherId, string? Color);
public sealed record UpdateSubjectRequest(
    string? Name = null,
    string? Short = null,
    Guid? TeacherId = null,
    string? Color = null,
    bool ClearTeacher = false);

// ---- Attendance (roll-call) ----
public sealed record AttendanceRecordResponse(
    Guid Id, Guid TenantId, Guid ClassId, Guid StudentId, DateTime Date, string Status, Guid? MarkedBy);
public sealed record AttendanceUpsertRow(Guid StudentId, string Status);
public sealed record BulkAttendanceRequest(DateTime Date, IReadOnlyList<AttendanceUpsertRow> Records);
/// Lightweight day-mark used by the absence-alert worker to compute streaks.
public sealed record AttendanceMarkRow(Guid StudentId, DateTime Date, string Status);

// ---- Staff/teacher attendance (admin roll-call for someone else, not self check-in) ----
public sealed record StaffAttendanceRecordResponse(
    Guid Id, Guid TenantId, string PersonType, Guid PersonId, DateTime Date, string Status, Guid? MarkedBy);
public sealed record StaffAttendanceUpsertRow(Guid PersonId, string Status);
public sealed record BulkStaffAttendanceRequest(
    string PersonType, DateTime Date, IReadOnlyList<StaffAttendanceUpsertRow> Records);

// ---- Exam attendance (roll-call for one exam paper — the paper already carries its own date) ----
public sealed record ExamAttendanceRecordResponse(
    Guid Id, Guid TenantId, Guid ExamPaperId, Guid StudentId, string Status, Guid? MarkedBy);
public sealed record ExamAttendanceUpsertRow(Guid StudentId, string Status);
public sealed record BulkExamAttendanceRequest(IReadOnlyList<ExamAttendanceUpsertRow> Records);

public sealed record AttendanceRollCallResponse(
    DateTime Date,
    string Day,
    int? Period,
    string? Subject,
    string? StartTime,
    string? EndTime,
    Guid? TeacherId,
    string? TeacherName,
    Guid? ClassTeacherId,
    string? ClassTeacherName,
    bool CanMark,
    string Reason,
    bool Marked);
