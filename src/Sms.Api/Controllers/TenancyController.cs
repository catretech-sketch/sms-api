using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Sms.Api.Controllers;

[Route("v1")]
[Authorize(Policy = "platform")]
public sealed class TenancyController : ApiControllerBase
{
    [HttpGet("tenancy/_ping")]
    public IActionResult Ping() => Ok(new { module = "tenancy", status = "live" });
}
