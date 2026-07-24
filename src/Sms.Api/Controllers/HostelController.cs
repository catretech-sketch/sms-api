using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sms.Application.Services.Hostel;
using Sms.Modules.Hostel.Contracts;
using Sms.Shared.Kernel.Authz;

namespace Sms.Api.Controllers;

[Route("v1/hostel")]
[Authorize(Policy = Policies.Principal)]
public sealed class HostelController(IHostelService hostel) : ApiControllerBase
{
    [HttpGet("summary")]
    public async Task<IActionResult> Summary(CancellationToken ct) =>
        FromResult(await hostel.GetSummaryAsync(ct));

    [HttpGet("blocks")]
    public async Task<IActionResult> ListBlocks(CancellationToken ct) =>
        FromResult(await hostel.ListBlocksAsync(ct));

    [HttpPost("blocks")]
    public async Task<IActionResult> CreateBlock([FromBody] CreateHostelBlockRequest req, CancellationToken ct) =>
        FromResult(await hostel.CreateBlockAsync(req, ct));

    [HttpGet("rooms")]
    public async Task<IActionResult> ListRooms(CancellationToken ct) =>
        FromResult(await hostel.ListRoomsAsync(ct));

    [HttpPost("rooms")]
    public async Task<IActionResult> CreateRoom([FromBody] CreateHostelRoomRequest req, CancellationToken ct) =>
        FromResult(await hostel.CreateRoomAsync(req, ct));

    [HttpGet("residents")]
    public async Task<IActionResult> ListResidents(CancellationToken ct) =>
        FromResult(await hostel.ListResidentsAsync(ct));

    [HttpPost("residents")]
    public async Task<IActionResult> CreateResident([FromBody] CreateHostelResidentRequest req, CancellationToken ct) =>
        FromResult(await hostel.CreateResidentAsync(req, ct));
}
