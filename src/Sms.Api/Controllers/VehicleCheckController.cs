using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sms.Application.Services.VehicleChecks;
using Sms.Modules.VehicleChecks;

namespace Sms.Api.Controllers;

[Route("v1/staff/vehicle-checks")]
[Authorize]
public sealed class VehicleCheckController(IVehicleCheckService vehicleChecks) : ApiControllerBase
{
    [HttpPost("inspections")]
    public async Task<IActionResult> SubmitInspection([FromBody] CreateInspectionRequest req, CancellationToken ct) =>
        FromResult(await vehicleChecks.SubmitInspectionAsync(req, User, ct));

    [HttpGet("inspections")]
    public async Task<IActionResult> ListInspections([FromQuery] Guid busId, CancellationToken ct) =>
        FromResult(await vehicleChecks.ListInspectionsAsync(busId, User, ct));

    [HttpPost("fuel-logs")]
    public async Task<IActionResult> SubmitFuelLog([FromBody] CreateFuelLogRequest req, CancellationToken ct) =>
        FromResult(await vehicleChecks.SubmitFuelLogAsync(req, User, ct));

    [HttpGet("fuel-logs")]
    public async Task<IActionResult> ListFuelLogs([FromQuery] Guid busId, CancellationToken ct) =>
        FromResult(await vehicleChecks.ListFuelLogsAsync(busId, User, ct));
}
