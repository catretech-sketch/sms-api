using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sms.Application.Services.Transport;
using Sms.Shared.Kernel.Authz;

namespace Sms.Api.Controllers;

/// Optional stop the child boards at, supplied when assigning a student to a bus.
public sealed record AssignStudentBusRequest(Guid? StopId);

/// School-admin transport surface (Operations screen). Distinct from the teacher-app /v1/bus routes.
[Route("v1/transport")]
[Authorize(Policy = Policies.Principal)]
public sealed class TransportController(IBusService bus, IStudentBusService studentBus) : ApiControllerBase
{
    [HttpGet("summary")]
    public async Task<IActionResult> Summary(CancellationToken ct) =>
        FromResult(await bus.GetSummaryAsync(ct));

    [HttpGet("fleet")]
    public async Task<IActionResult> Fleet(CancellationToken ct) =>
        FromResult(await bus.GetFleetAsync(ct));

    [HttpGet("buses/{busId:guid}/students")]
    public async Task<IActionResult> BusStudents(Guid busId, CancellationToken ct) =>
        FromResult(await studentBus.ListByBusAsync(busId, ct));

    [HttpPut("buses/{busId:guid}/students/{studentId:guid}")]
    public async Task<IActionResult> AssignStudent(
        Guid busId, Guid studentId, [FromBody] AssignStudentBusRequest? req, CancellationToken ct) =>
        FromResult(await studentBus.AssignAsync(busId, studentId, req?.StopId, ct));

    [HttpDelete("buses/{busId:guid}/students/{studentId:guid}")]
    public async Task<IActionResult> UnassignStudent(Guid busId, Guid studentId, CancellationToken ct) =>
        FromResult(await studentBus.UnassignAsync(studentId, ct));
}
