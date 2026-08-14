using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sms.Application.Services.Academics;
using Sms.Application.Services.Sis;
using Sms.Modules.Sis.Contracts;
using Sms.Shared.Kernel.Authz;

namespace Sms.Api.Controllers;

[Route("v1")]
[Authorize]
public sealed class StudentController(ISisService sis, IAcademicsService academics) : ApiControllerBase
{
    [HttpGet("students")]
    public async Task<IActionResult> List(
        [FromQuery] string? q, [FromQuery] string? grade, [FromQuery] string? status, [FromQuery] string? fee,
        CancellationToken ct) =>
        FromCursorResult(await sis.ListStudentsAsync(q, grade, status, fee, ct));

    [HttpGet("students/me")]
    public async Task<IActionResult> Me(CancellationToken ct) =>
        FromResult(await sis.GetMyStudentAsync(ct));

    [HttpGet("students/{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) =>
        FromResult(await sis.GetStudentAsync(id, ct));

    [HttpPost("students")]
    public async Task<IActionResult> Create([FromBody] CreateStudentRequest req, CancellationToken ct) =>
        FromResult(await sis.CreateStudentAsync(req, ct));

    [HttpPatch("students/{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateStudentRequest req, CancellationToken ct) =>
        FromResult(await sis.UpdateStudentAsync(id, req, ct));

    [HttpGet("classes/{classId:guid}/students")]
    [Authorize(Policy = AuthorizationPolicies.TeacherApp)]
    public async Task<IActionResult> ListByClass(
        Guid classId, [FromQuery] int? limit, [FromQuery] string? cursor, CancellationToken ct) =>
        FromCursorResult(await sis.ListClassStudentsAsync(classId, limit, cursor, ct));

    [HttpGet("students/{studentId:guid}/attendance")]
    public async Task<IActionResult> ListAttendance(
        Guid studentId, [FromQuery] DateTime? from, [FromQuery] DateTime? to, CancellationToken ct) =>
        FromResult(await academics.ListAttendanceForStudentAsync(
            studentId, from ?? DateTime.UtcNow.AddDays(-90), to ?? DateTime.UtcNow, User, ct));

    [HttpGet("students/{studentId:guid}/attendance/periods")]
    public async Task<IActionResult> ListPeriodAttendance(
        Guid studentId, [FromQuery] DateTime? from, [FromQuery] DateTime? to, CancellationToken ct) =>
        FromResult(await academics.ListPeriodAttendanceForStudentAsync(
            studentId, from ?? DateTime.UtcNow.AddDays(-90), to ?? DateTime.UtcNow, User, ct));

    /// <summary>
    /// Official period-based attendance aggregate for the student (CRM / Teacher / Student / Parent).
    /// Percentage = (present + late) / marked periods × 100; null when unmarked.
    /// </summary>
    [HttpGet("students/{studentId:guid}/attendance/summary")]
    public async Task<IActionResult> GetPeriodAttendanceSummary(
        Guid studentId, [FromQuery] DateTime? from, [FromQuery] DateTime? to, CancellationToken ct) =>
        FromResult(await academics.GetPeriodAttendanceSummaryForStudentAsync(
            studentId, from ?? DateTime.UtcNow.AddDays(-365), to ?? DateTime.UtcNow, User, ct));
}
