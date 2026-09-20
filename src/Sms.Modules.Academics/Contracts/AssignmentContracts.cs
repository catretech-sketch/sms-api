namespace Sms.Modules.Academics.Contracts;

public sealed record AssignmentResponse(
    Guid Id, Guid TenantId, string Title, Guid? ClassId, string? ClassName, string? Subject, DateTime? DueDate,
    int SubmissionsCount, int TotalStudents, string Status, string? Description, string? ImageUri, int? Period = null);

public sealed record CreateAssignmentRequest(
    string Title, Guid? ClassId, string? ClassName, string? Subject, DateTime? DueDate, string? Description, string? ImageUri,
    int? Period = null);
