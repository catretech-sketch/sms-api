using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sms.Application.Services.Transport;
using Sms.Modules.Transport;

namespace Sms.Api.Controllers;

[Route("v1/staff")]
[Authorize]
public sealed class TripController(ITripService trips) : ApiControllerBase
{
    [HttpPost("trips")]
    public async Task<IActionResult> Start([FromBody] StartTripRequest req, CancellationToken ct) =>
        FromResult(await trips.StartAsync(req, ct));

    [HttpGet("trip/current")]
    public async Task<IActionResult> GetCurrent(CancellationToken ct) =>
        FromResult(await trips.GetCurrentAsync(ct));

    [HttpPost("trips/{tripId:guid}/pings")]
    public async Task<IActionResult> IngestPings(Guid tripId, [FromBody] BulkPingRequest req, CancellationToken ct) =>
        FromResult(await trips.IngestPingsAsync(tripId, req, ct));

    [HttpPost("trips/{tripId:guid}/end")]
    public async Task<IActionResult> End(Guid tripId, CancellationToken ct) =>
        FromResult(await trips.EndAsync(tripId, ct));

    [HttpGet("trips/{tripId:guid}/boarding")]
    public async Task<IActionResult> ListBoarding(Guid tripId, CancellationToken ct) =>
        FromResult(await trips.ListBoardingAsync(tripId, ct));

    [HttpPost("trips/{tripId:guid}/boarding")]
    public async Task<IActionResult> UpsertBoarding(Guid tripId, [FromBody] BoardingRequest req, CancellationToken ct) =>
        FromResult(await trips.UpsertBoardingAsync(tripId, req, ct));
}
