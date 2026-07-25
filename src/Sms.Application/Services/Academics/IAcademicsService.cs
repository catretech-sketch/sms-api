using System.Security.Claims;
using Sms.Application.Common;
using Sms.Modules.Academics.Contracts;

namespace Sms.Application.Services.Academics;

public interface IAcademicsService
{
    Task<ApiResult<IReadOnlyList<ClassResponse>>> ListClassesAsync(CancellationToken ct = default);
    Task<ApiResult<ClassResponse>> GetClassAsync(Guid id, CancellationToken ct = default);
    Task<ApiResult<ClassResponse>> CreateClassAsync(CreateClassRequest req, CancellationToken ct = default);
    Task<ApiResult<ClassResponse>> UpdateClassAsync(Guid id, UpdateClassRequest req, CancellationToken ct = default);

    Task<ApiResult<IReadOnlyList<SubjectResponse>>> ListSubjectsAsync(CancellationToken ct = default);
    Task<ApiResult<SubjectResponse>> GetSubjectAsync(Guid id, CancellationToken ct = default);
    Task<ApiResult<SubjectResponse>> CreateSubjectAsync(CreateSubjectRequest req, CancellationToken ct = default);
    Task<ApiResult<SubjectResponse>> UpdateSubjectAsync(Guid id, UpdateSubjectRequest req, CancellationToken ct = default);
    Task<ApiResult> DeleteSubjectAsync(Guid id, CancellationToken ct = default);

    Task<ApiResult<IReadOnlyList<AttendanceRecordResponse>>> ListAttendanceAsync(
        Guid classId, DateTime date, CancellationToken ct = default);
    Task<ApiResult> BulkUpsertAttendanceAsync(
        Guid classId, BulkAttendanceRequest req, CancellationToken ct = default);

    Task<ApiResult<IReadOnlyList<ExamResponse>>> ListExamsAsync(CancellationToken ct = default);
    Task<ApiResult<ExamResponse>> GetExamAsync(Guid id, CancellationToken ct = default);
    Task<ApiResult<ExamResponse>> CreateExamAsync(CreateExamRequest req, CancellationToken ct = default);
    Task<ApiResult<ExamResponse>> UpdateExamAsync(Guid id, UpdateExamRequest req, CancellationToken ct = default);

    Task<ApiResult<IReadOnlyList<ExamPaperResponse>>> ListExamPapersAsync(
        Guid? examId, CancellationToken ct = default);
    Task<ApiResult<ExamPaperResponse>> GetExamPaperAsync(Guid id, CancellationToken ct = default);
    Task<ApiResult<ExamPaperResponse>> CreateExamPaperAsync(CreateExamPaperRequest req, CancellationToken ct = default);
    Task<ApiResult<ExamPaperResponse>> UpdateExamPaperAsync(Guid id, UpdateExamPaperRequest req, CancellationToken ct = default);
    Task<ApiResult> DeleteExamPaperAsync(Guid id, CancellationToken ct = default);

    Task<ApiResult<IReadOnlyList<GradeResponse>>> ListGradesAsync(Guid examPaperId, CancellationToken ct = default);
    Task<ApiResult<GradeResponse>> UpsertGradeAsync(UpsertGradeRequest req, CancellationToken ct = default);

    Task<ApiResult<IReadOnlyList<HomeworkResponse>>> ListHomeworkAsync(
        Guid? studentId, string? status, CancellationToken ct = default);
    Task<ApiResult<HomeworkResponse>> GetHomeworkAsync(Guid id, CancellationToken ct = default);
    Task<ApiResult<HomeworkResponse>> CreateHomeworkAsync(CreateHomeworkRequest req, CancellationToken ct = default);
    Task<ApiResult<HomeworkResponse>> SetHomeworkStatusAsync(
        Guid id, SetHomeworkStatusRequest req, CancellationToken ct = default);
    Task<ApiResult<HomeworkResponse>> SubmitHomeworkAsync(Guid id, CancellationToken ct = default);

    Task<ApiResult<IReadOnlyList<TimetableSlotResponse>>> ListTimetableAsync(
        ClaimsPrincipal caller, CancellationToken ct = default);
    Task<ApiResult<TimetableSlotResponse>> CreateTimetableSlotAsync(
        CreateTimetableSlotRequest req, CancellationToken ct = default);

    Task<ApiResult<IReadOnlyList<CalendarEventResponse>>> ListCalendarEventsAsync(CancellationToken ct = default);
    Task<ApiResult<CalendarEventResponse>> CreateCalendarEventAsync(
        CreateCalendarEventRequest req, CancellationToken ct = default);

    Task<ApiResult<IReadOnlyList<LibraryBookResponse>>> ListLibraryBooksAsync(CancellationToken ct = default);
    Task<ApiResult<LibrarySummaryResponse>> GetLibrarySummaryAsync(CancellationToken ct = default);
    Task<ApiResult<LibraryBookResponse>> CreateLibraryBookAsync(
        CreateLibraryBookRequest req, CancellationToken ct = default);

    Task<ApiResult<IReadOnlyList<AssignmentResponse>>> ListAssignmentsAsync(CancellationToken ct = default);
    Task<ApiResult<AssignmentResponse>> CreateAssignmentAsync(
        CreateAssignmentRequest req, CancellationToken ct = default);
}
