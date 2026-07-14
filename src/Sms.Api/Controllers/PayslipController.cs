using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sms.Application.Services.Finance;
using Sms.Modules.Finance;

namespace Sms.Api.Controllers;

[Route("v1")]
[Authorize]
public sealed class PayslipController(IPayslipService payslips) : ApiControllerBase
{
    [HttpGet("payslips")]
    public async Task<IActionResult> List([FromQuery(Name = "user_id")] Guid? userId, CancellationToken ct) =>
        FromResult(await payslips.ListAsync(userId, ct));

    [HttpPost("payslips")]
    public async Task<IActionResult> Create([FromBody] CreatePayslipRequest req, CancellationToken ct) =>
        FromResult(await payslips.CreateAsync(req, ct));
}
