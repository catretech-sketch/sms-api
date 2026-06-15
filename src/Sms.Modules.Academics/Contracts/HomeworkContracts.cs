namespace Sms.Modules.Academics.Contracts;

public sealed record HomeworkResponse(
    Guid Id, Guid TenantId, Guid StudentId, Guid? AssignmentId, string Title, Guid? SubjectId,
    DateTime? DueDate, string? DueTime, string Status, string Priority, string? Grade);

public sealed record CreateHomeworkRequest(
    Guid StudentId, Guid? AssignmentId, string Title, Guid? SubjectId, DateTime? DueDate, string? DueTime, string? Priority);

public sealed record SetHomeworkStatusRequest(string Status);
