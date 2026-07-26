using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sms.Application.Services.Academics;
using Sms.Modules.Academics.Contracts;

namespace Sms.Api.Controllers;

/// Admin/principal roll-call marking for teachers/staff — distinct from
/// AttendanceController, which is the logged-in user's own check-in/out punches.
[Route("v1/staff-attendance")]
[Authorize]
public sealed class StaffAttendanceController(IAcademicsService academics) : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery(Name = "person_type")] string personType, [FromQuery] DateTime date, CancellationToken ct) =>
        FromResult(await academics.ListStaffAttendanceAsync(personType, date, ct));

    [HttpPost]
    public async Task<IActionResult> BulkUpsert([FromBody] BulkStaffAttendanceRequest req, CancellationToken ct) =>
        FromResult(await academics.BulkUpsertStaffAttendanceAsync(req, ct));

    [HttpGet("{personId:guid}")]
    public async Task<IActionResult> ListForPerson(
        Guid personId, [FromQuery(Name = "person_type")] string personType,
        [FromQuery] DateTime from, [FromQuery] DateTime to, CancellationToken ct) =>
        FromResult(await academics.ListStaffAttendanceForPersonAsync(personType, personId, from, to, ct));
}
