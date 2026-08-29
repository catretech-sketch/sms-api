using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Sms.Application.Services.AiSearch;

namespace Sms.Api.Controllers;

/// Single centralized read-only NL search endpoint consumed by every app
/// (CRM/Admin, Principal, Teacher, Student, Parent, Staff). See
/// docs/superpowers/specs/2026-08-28-ai-search-design.md.
[Route("v1/ai")]
[Authorize]
[EnableRateLimiting("ai-search")]
public sealed class AiSearchController(IAiSearchService search) : ApiControllerBase
{
    [HttpPost("search")]
    public async Task<IActionResult> Search([FromBody] AiSearchRequest request, CancellationToken ct)
    {
        var roles = User.FindAll("role").Select(c => c.Value).ToList();
        var response = await search.SearchAsync(request, roles, ct);
        if (response.Success || response.Error is null)
            return Ok(response);

        // "FeatureNotEnabled" and "InvalidRequest" are client-facing outcomes the caller can act on
        // (upgrade plan / fix the query) -> 403 / 400. "SearchFailed" is the one error code the
        // orchestrator produces when a handler throws (SQL timeout, deadlock, etc.) rather than
        // returning a well-formed refusal -- a genuine internal failure, not a bad request, so it
        // gets the same 500 the rest of this API reserves for unhandled exceptions
        // (see GlobalExceptionHandler), even though here it is a normal AiSearchResponse rather than
        // an escaping exception.
        var status = response.Error.Code switch
        {
            "FeatureNotEnabled" => 403,
            "SearchFailed" => 500,
            _ => 400,
        };
        return StatusCode(status, response);
    }
}
