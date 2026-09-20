using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sms.Application.Services.Sports;
using Sms.Modules.Sports.Contracts;
using Sms.Shared.Kernel.Authz;

namespace Sms.Api.Controllers;

[Route("v1/sports")]
[Authorize(Policy = Policies.Principal)]
public sealed class SportsController(ISportsService sports) : ApiControllerBase
{
    [HttpGet("summary")]
    public async Task<IActionResult> Summary(CancellationToken ct) =>
        FromResult(await sports.GetSummaryAsync(ct));

    [HttpGet("teams")]
    public async Task<IActionResult> ListTeams(CancellationToken ct) =>
        FromResult(await sports.ListTeamsAsync(ct));

    [HttpPost("teams")]
    public async Task<IActionResult> CreateTeam([FromBody] CreateSportsTeamRequest req, CancellationToken ct) =>
        FromResult(await sports.CreateTeamAsync(req, ct));

    [HttpGet("events")]
    public async Task<IActionResult> ListEvents(CancellationToken ct) =>
        FromResult(await sports.ListEventsAsync(ct));

    [HttpPost("events")]
    public async Task<IActionResult> CreateEvent([FromBody] CreateSportsEventRequest req, CancellationToken ct) =>
        FromResult(await sports.CreateEventAsync(req, ct));

    [HttpGet("medals")]
    public async Task<IActionResult> ListMedals(CancellationToken ct) =>
        FromResult(await sports.ListMedalsAsync(ct));

    [HttpPost("medals")]
    public async Task<IActionResult> CreateMedal([FromBody] CreateSportsMedalRequest req, CancellationToken ct) =>
        FromResult(await sports.CreateMedalAsync(req, ct));
}
