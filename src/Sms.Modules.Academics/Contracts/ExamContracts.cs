namespace Sms.Modules.Academics.Contracts;

// ---- Exam (term) ----
public sealed record ExamResponse(
    Guid Id, Guid TenantId, string Name, string? Type, string? Grades, DateTime? FromDate, DateTime? ToDate,
    int SubjectCount, string Status, decimal MarksEnteredPct, bool Published);
public sealed record CreateExamRequest(
    string Name, string? Type, string? Grades, DateTime? FromDate, DateTime? ToDate, int SubjectCount);
public sealed record UpdateExamRequest(string? Status, bool? Published, decimal? MarksEnteredPct);

// ---- ExamPaper (shared resource) ----
public sealed record ExamPaperResponse(
    Guid Id, Guid TenantId, Guid? ExamId, Guid? ClassId, string? Name, string? Subject, Guid? SubjectId,
    DateTime? Date, string? StartTime, int? DurationMin, int MaxMarks, string? Room,
    string? Invigilator1, string? Invigilator2, string Status);
public sealed record CreateExamPaperRequest(
    Guid? ExamId, Guid? ClassId, string? Name, string? Subject, Guid? SubjectId, DateTime? Date,
    string? StartTime, int? DurationMin, int MaxMarks, string? Room, string? Invigilator1, string? Invigilator2);

public sealed record UpdateExamPaperRequest(
    string? Name, string? Subject, Guid? SubjectId, DateTime? Date, string? StartTime, int? DurationMin,
    int? MaxMarks, string? Room, string? Invigilator1, string? Invigilator2, string? Status);

// ---- Grade ----
public sealed record GradeResponse(
    Guid Id, Guid TenantId, Guid StudentId, string? StudentName, Guid ExamPaperId, decimal Marks,
    decimal MaxMarks, string? Grade, decimal Gpa, bool Pass, DateTime? Date);
public sealed record UpsertGradeRequest(Guid StudentId, string? StudentName, Guid ExamPaperId, decimal Marks);
