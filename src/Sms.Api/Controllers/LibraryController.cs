using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sms.Application.Services.Academics;
using Sms.Modules.Academics.Contracts;
using Sms.Shared.Kernel.Authz;

namespace Sms.Api.Controllers;

[Route("v1")]
[Authorize]
public sealed class LibraryController(IAcademicsService academics) : ApiControllerBase
{
    [HttpGet("library")]
    [Authorize(Policy = AuthorizationPolicies.TeacherApp)]
    public async Task<IActionResult> List(CancellationToken ct) =>
        FromResult(await academics.ListLibraryBooksAsync(ct));

    [HttpGet("library/summary")]
    [Authorize(Policy = Policies.Principal)]
    public async Task<IActionResult> Summary(CancellationToken ct) =>
        FromResult(await academics.GetLibrarySummaryAsync(ct));

    [HttpPost("library")]
    [Authorize(Policy = Policies.Principal)]
    public async Task<IActionResult> Create([FromBody] CreateLibraryBookRequest req, CancellationToken ct) =>
        FromResult(await academics.CreateLibraryBookAsync(req, ct));
}
