using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sms.Application.Services.Tasks;
using Sms.Modules.Tasks;

namespace Sms.Api.Controllers;

[Route("v1")]
[Authorize]
public sealed class TaskController(ITaskService tasks) : ApiControllerBase
{
    // Staff-facing: always "my tasks" (assigned to me, or broadcast to my duty role), regardless
    // of the caller's own role — mirrors the mobile app's httpTasks contract exactly, and even a
    // manager hitting this route from the app gets their own tasks, not the tenant's. Managers get
    // the full tenant view from GET /v1/staff/tasks/all instead.
    [HttpGet("staff/tasks")]
    public async Task<IActionResult> ListMine(CancellationToken ct) =>
        FromResult(await tasks.ListMineAsync(User, ct));

    [HttpPost("staff/tasks/{id:guid}/complete")]
    public async Task<IActionResult> Complete(Guid id, CancellationToken ct) =>
        FromResult(await tasks.CompleteAsync(id, User, ct));

    [HttpPost("staff/tasks/{id:guid}/photo")]
    public async Task<IActionResult> AttachPhoto(Guid id, [FromBody] AttachPhotoRequest req, CancellationToken ct) =>
        FromResult(await tasks.AttachPhotoAsync(id, req.PhotoBase64, User, ct));

    // Manager/CRM-facing.
    [HttpPost("staff/tasks")]
    public async Task<IActionResult> Create([FromBody] CreateTaskRequest req, CancellationToken ct) =>
        FromResult(await tasks.CreateAsync(req, User, ct));

    [HttpGet("staff/tasks/all")]
    public async Task<IActionResult> ListAll(CancellationToken ct) =>
        FromResult(await tasks.ListAllAsync(User, ct));
}

public sealed record AttachPhotoRequest(string? PhotoBase64);
