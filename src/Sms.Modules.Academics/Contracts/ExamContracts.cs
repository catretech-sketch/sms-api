namespace Sms.Modules.Academics.Contracts;

// ---- Exam (term) ----
public sealed record ExamResponse(
    Guid Id, Guid TenantId, string Name, string? Type, string? Grades, DateTime? FromDate, DateTime? ToDate,
    int SubjectCount, string Status, decimal MarksEnteredPct, bool Published,
    IReadOnlyList<Guid>? ClassIds = null);
public sealed record CreateExamRequest(
    string Name, string? Type, string? Grades, DateTime? FromDate, DateTime? ToDate, int SubjectCount,
    IReadOnlyList<Guid>? ClassIds = null);
public sealed record UpdateExamRequest(
    string? Status, bool? Published, decimal? MarksEnteredPct, IReadOnlyList<Guid>? ClassIds = null);

// ---- ExamPaper (shared resource) ----
public sealed record ExamPaperResponse(
    Guid Id, Guid TenantId, Guid? ExamId, Guid? ClassId, string? Name, string? Subject, Guid? SubjectId,
    DateTime? Date, string? StartTime, int? DurationMin, int MaxMarks, string? Room,
    string? Invigilator1, string? Invigilator2, string Status, string? Topics = null);
public sealed record CreateExamPaperRequest(
    Guid? ExamId, Guid? ClassId, string? Name, string? Subject, Guid? SubjectId, DateTime? Date,
    string? StartTime, int? DurationMin, int MaxMarks, string? Room, string? Invigilator1, string? Invigilator2,
    string? Topics = null);

public sealed record UpdateExamPaperRequest(
    string? Name, string? Subject, Guid? SubjectId, DateTime? Date, string? StartTime, int? DurationMin,
    int? MaxMarks, string? Room, string? Invigilator1, string? Invigilator2, string? Status, string? Topics = null);

// ---- Grade ----
/// Property bag so Dapper can map both paper-scoped lists (11 cols) and
/// student report-card lists (+ SubjectId/Subject/PaperName/ExamPublished).
public sealed class GradeResponse
{
    public Guid Id { get; init; }
    public Guid TenantId { get; init; }
    public Guid StudentId { get; init; }
    public string? StudentName { get; init; }
    public Guid ExamPaperId { get; init; }
    public decimal Marks { get; init; }
    public decimal MaxMarks { get; init; }
    public string? Grade { get; init; }
    public decimal Gpa { get; init; }
    public bool Pass { get; init; }
    public DateTime? Date { get; init; }
    public Guid? SubjectId { get; init; }
    public string? Subject { get; init; }
    public string? PaperName { get; init; }
    public bool ExamPublished { get; init; }
}
public sealed record UpsertGradeRequest(Guid StudentId, string? StudentName, Guid ExamPaperId, decimal Marks);
