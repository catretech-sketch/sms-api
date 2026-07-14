using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Sms.Api.Controllers;

[Route("health")]
[AllowAnonymous]
public sealed class HealthController : ApiControllerBase
{
    [HttpGet]
    public IActionResult Get() => Ok(new { status = "ok" });
}
