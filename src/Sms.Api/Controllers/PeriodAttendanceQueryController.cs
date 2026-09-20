using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sms.Application.Services.Academics;

namespace Sms.Api.Controllers;

[Route("v1/attendance")]
[Authorize]
public sealed class PeriodAttendanceQueryController(IAcademicsService academics) : ApiControllerBase
{
    [HttpGet("period-records")]
    public async Task<IActionResult> ListPeriodRecords(
        [FromQuery] string? preset,
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] Guid? classId,
        [FromQuery] string? grade,
        [FromQuery] string? section,
        [FromQuery] string? subject,
        [FromQuery] int? period,
        [FromQuery] Guid? assignedTeacherId,
        [FromQuery] Guid? markedBy,
        [FromQuery] string? markedByRole,
        [FromQuery] string? status,
        [FromQuery] string? q,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        [FromQuery] string? geoFenceStatus = null,
        CancellationToken ct = default) =>
        FromResult(await academics.ListPeriodAttendanceAdvancedAsync(
            User,
            preset,
            from,
            to,
            classId,
            grade,
            section,
            subject,
            period,
            assignedTeacherId,
            markedBy,
            markedByRole,
            status,
            q,
            page,
            pageSize,
            geoFenceStatus,
            ct));

    [HttpGet("period-records/{id:guid}/audit")]
    public async Task<IActionResult> GetAudit(Guid id, CancellationToken ct = default) =>
        FromResult(await academics.GetPeriodAttendanceAuditAsync(id, User, ct));

    [HttpGet("period-records/summary/class")]
    public async Task<IActionResult> GetClassDaySummary(
        [FromQuery] Guid classId,
        [FromQuery] DateOnly date,
        CancellationToken ct = default) =>
        FromResult(await academics.GetPeriodAttendanceClassDaySummaryAsync(
            User, classId, date, ct));

    [HttpGet("period-records/summary/subjects")]
    public async Task<IActionResult> ListSubjectSummaries(
        [FromQuery] Guid classId,
        [FromQuery] string? preset,
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        CancellationToken ct = default) =>
        FromResult(await academics.ListPeriodAttendanceSubjectSummariesAsync(
            User, classId, preset, from, to, ct));

    [HttpGet("period-records/summary/teachers")]
    public async Task<IActionResult> ListTeacherSummaries(
        [FromQuery] string? preset,
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        CancellationToken ct = default) =>
        FromResult(await academics.ListPeriodAttendanceTeacherSummariesAsync(
            User, preset, from, to, ct));

    [HttpGet("period-records/summary/range")]
    public async Task<IActionResult> GetRangeSummary(
        [FromQuery] string? preset,
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] Guid? classId,
        [FromQuery] string? grade,
        [FromQuery] string? section,
        [FromQuery] Guid? studentId,
        [FromQuery] string? subject,
        [FromQuery] Guid? teacherId,
        CancellationToken ct = default) =>
        FromResult(await academics.GetPeriodAttendanceRangeSummaryAsync(
            User,
            preset,
            from,
            to,
            classId,
            grade,
            section,
            studentId,
            subject,
            teacherId,
            ct));
}
