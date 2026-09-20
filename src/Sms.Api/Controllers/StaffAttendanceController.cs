using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sms.Application.Common;
using Sms.Application.Services.Academics;
using Sms.Modules.Academics.Contracts;
using Sms.Shared.Kernel.Results;

namespace Sms.Api.Controllers;

/// Admin/principal roll-call marking for teachers/staff (Present / Half day / Absent).
/// Distinct from AttendanceController, which is the logged-in user's own check-in/out punches.
[Route("v1/staff-attendance")]
[Authorize]
public sealed class StaffAttendanceController(IAcademicsService academics) : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery(Name = "person_type")] string personType,
        [FromQuery] DateTime? date,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        CancellationToken ct)
    {
        if (from is { } f && to is { } t)
            return FromResult(await academics.ListStaffAttendanceRangeAsync(personType, f, t, ct));
        if (date is { } d)
            return FromResult(await academics.ListStaffAttendanceAsync(personType, d, ct));
        return FromResult(ApiResult<IReadOnlyList<StaffAttendanceRecordResponse>>.Fail(
            new Error("invalid_request", "Provide date, or from and to."), 400));
    }

    [HttpPost]
    public async Task<IActionResult> BulkUpsert([FromBody] BulkStaffAttendanceRequest req, CancellationToken ct) =>
        FromResult(await academics.BulkUpsertStaffAttendanceAsync(req, User, ct));

    [HttpGet("{personId:guid}")]
    public async Task<IActionResult> ListForPerson(
        Guid personId, [FromQuery(Name = "person_type")] string personType,
        [FromQuery] DateTime from, [FromQuery] DateTime to, CancellationToken ct) =>
        FromResult(await academics.ListStaffAttendanceForPersonAsync(personType, personId, from, to, ct));
}
