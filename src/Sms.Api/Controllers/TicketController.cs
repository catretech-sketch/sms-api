using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sms.Application.Services.Tenancy;
using Sms.Modules.Tenancy.Contracts;

namespace Sms.Api.Controllers;

[Route("v1")]
[Authorize(Policy = "platform")]
public sealed class TicketController(ITenancyService tenancy) : ApiControllerBase
{
    [HttpGet("tickets")]
    public async Task<IActionResult> List(string? status, string? q, CancellationToken ct) =>
        CursorOk(await tenancy.ListTicketsAsync(status, q, ct));

    [HttpGet("tickets/{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) =>
        FromResult(await tenancy.GetTicketAsync(id, ct));

    [HttpPost("tickets")]
    public async Task<IActionResult> Create([FromBody] CreateTicketRequest req, CancellationToken ct) =>
        FromResult(await tenancy.CreateTicketAsync(req, ct));

    [HttpPatch("tickets/{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateTicketRequest req, CancellationToken ct) =>
        FromResult(await tenancy.UpdateTicketAsync(id, req, ct));

    [HttpPost("tickets/{id:guid}/messages")]
    public async Task<IActionResult> AddMessage(Guid id, [FromBody] AddMessageRequest req, CancellationToken ct)
    {
        var who = User.FindFirst("sub")?.Value ?? "agent";
        return FromResult(await tenancy.AddTicketMessageAsync(id, req, who, ct));
    }
}
