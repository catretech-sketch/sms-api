using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sms.Application.Services.Finance;
using Sms.Modules.Finance;
using Sms.Shared.Kernel.Authz;

namespace Sms.Api.Controllers;

[Route("v1/payroll")]
[Authorize]
public sealed class PayrollController(IPayrollService payroll) : ApiControllerBase
{
    [HttpGet("salary-profiles")]
    public async Task<IActionResult> ListSalaryProfiles(CancellationToken ct) =>
        FromResult(await payroll.ListSalaryProfilesAsync(ct));

    [HttpPut("salary-profiles/{personType}/{personId:guid}")]
    public async Task<IActionResult> UpsertSalaryProfile(
        string personType, Guid personId, [FromBody] UpsertSalaryProfileRequest req, CancellationToken ct) =>
        FromResult(await payroll.UpsertSalaryProfileAsync(personType, personId, req, ct));

    [HttpGet("salary-structures")]
    public async Task<IActionResult> ListSalaryStructures(CancellationToken ct) =>
        FromResult(await payroll.ListSalaryStructuresAsync(ct));

    [HttpPut("salary-structures")]
    public async Task<IActionResult> UpsertSalaryStructure(
        [FromBody] UpsertSalaryStructureRequest req, CancellationToken ct) =>
        FromResult(await payroll.UpsertSalaryStructureAsync(req, ct));

    [HttpGet("runs/{period}")]
    public async Task<IActionResult> GetRun(string period, [FromQuery] bool preview, CancellationToken ct) =>
        FromResult(await payroll.GetRunAsync(period, preview, ct));

    [HttpPost("runs/{period}/run")]
    public async Task<IActionResult> Run(string period, CancellationToken ct) =>
        FromResult(await payroll.RunAsync(period, ct));

    [HttpPost("runs/{period}/approve")]
    [Authorize(Policy = Policies.Principal)]
    public async Task<IActionResult> Approve(string period, CancellationToken ct) =>
        FromResult(await payroll.ApproveAsync(period, ct));
}
