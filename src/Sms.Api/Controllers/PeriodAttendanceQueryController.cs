using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sms.Application.Services.Academics;
using Sms.Shared.Kernel.Authz;

namespace Sms.Api.Controllers;

[Route("v1/attendance")]
[Authorize(Policy = AuthorizationPolicies.TeacherApp)]
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
            ct));
}
