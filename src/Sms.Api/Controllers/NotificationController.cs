using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sms.Application.Services.Comms;
using Sms.Modules.Comms;

namespace Sms.Api.Controllers;

[Route("v1")]
[Authorize]
public sealed class NotificationController(INotificationService notifications) : ApiControllerBase
{
    [HttpGet("notifications")]
    public async Task<IActionResult> List(CancellationToken ct) =>
        FromResult(await notifications.ListAsync(ct));

    [HttpPost("notifications/read")]
    public async Task<IActionResult> MarkRead(CancellationToken ct) =>
        FromResult(await notifications.MarkReadAsync(ct));

    [HttpPost("notifications")]
    public async Task<IActionResult> Create([FromBody] CreateNotificationRequest req, CancellationToken ct) =>
        FromResult(await notifications.CreateAsync(req, ct));
}
