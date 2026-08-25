using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sms.Application.Services.Finance;
using Sms.Modules.Finance;
using Sms.Shared.Kernel.Authz;

namespace Sms.Api.Controllers;

[Route("v1")]
[Authorize]
public sealed class PayslipController(IPayslipService payslips) : ApiControllerBase
{
    [HttpGet("payslips")]
    public async Task<IActionResult> List([FromQuery(Name = "user_id")] Guid? userId, CancellationToken ct)
    {
        // Non-staff callers can only read their own slips; ignore client-supplied user_id.
        var scoped = RoleChecks.IsStaff(User) ? userId : null;
        return FromResult(await payslips.ListAsync(scoped, ct));
    }

    [HttpPost("payslips")]
    public async Task<IActionResult> Create([FromBody] CreatePayslipRequest req, CancellationToken ct)
    {
        if (!RoleChecks.IsStaff(User))
            return ForbiddenResult("staff only");
        return FromResult(await payslips.CreateAsync(req, ct));
    }
}
