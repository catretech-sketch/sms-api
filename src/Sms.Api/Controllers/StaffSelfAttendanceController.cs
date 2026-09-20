using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sms.Application.Services.Attendance;
using Sms.Modules.Attendance;

namespace Sms.Api.Controllers;

[Route("v1/staff")]
[Authorize]
public sealed class StaffSelfAttendanceController(IStaffAttendanceService attendance) : ApiControllerBase
{
    [HttpGet("attendance")]
    public async Task<IActionResult> Get([FromQuery] int? offset_minutes, CancellationToken ct) =>
        FromResult(await attendance.GetTodayAsync(offset_minutes, ct));

    [HttpPost("attendance/check-in")]
    public async Task<IActionResult> CheckIn([FromBody] StaffCheckRequest req, CancellationToken ct) =>
        FromResult(await attendance.CheckInAsync(req, ct));

    [HttpPost("attendance/check-out")]
    public async Task<IActionResult> CheckOut([FromBody] StaffCheckRequest req, CancellationToken ct) =>
        FromResult(await attendance.CheckOutAsync(req, ct));
}
