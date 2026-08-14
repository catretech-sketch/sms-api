using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sms.Application.Common;
using Sms.Application.Services.Academics;
using Sms.Application.Services.Sis;
using Sms.Modules.Academics.Contracts;
using Sms.Shared.Kernel.Results;

namespace Sms.Api.Controllers;

[Route("v1")]
[Authorize]
public sealed class HomeworkController(IAcademicsService academics, ISisService sis) : ApiControllerBase
{
    [HttpGet("homework")]
    public async Task<IActionResult> List(
        [FromQuery(Name = "student_id")] Guid? studentId, [FromQuery] string? status, CancellationToken ct)
    {
        var resolved = await ResolveStudentIdAsync(studentId, ct);
        if (resolved.Error is not null)
            return FromResult(resolved);
        return FromResult(await academics.ListHomeworkAsync(resolved.Data, status, ct));
    }

    [HttpGet("homework/{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) =>
        FromResult(await academics.GetHomeworkAsync(id, ct));

    [HttpPost("homework")]
    public async Task<IActionResult> Create([FromBody] CreateHomeworkRequest req, CancellationToken ct) =>
        FromResult(await academics.CreateHomeworkAsync(req, ct));

    [HttpPatch("homework/{id:guid}")]
    public async Task<IActionResult> SetStatus(
        Guid id, [FromBody] SetHomeworkStatusRequest req, CancellationToken ct) =>
        FromResult(await academics.SetHomeworkStatusAsync(id, req, ct));

    [HttpPost("homework/{id:guid}/submit")]
    public async Task<IActionResult> Submit(Guid id, CancellationToken ct) =>
        FromResult(await academics.SubmitHomeworkAsync(id, ct));

    /// <summary>
    /// Login Users.Id is not the SIS Students.Id. Student/parent callers are always
    /// scoped to the roster row linked by Users.StudentId (admission number).
    /// Teachers/admins may pass a real SIS id or omit it to list all.
    /// </summary>
    private async Task<ApiResult<Guid?>> ResolveStudentIdAsync(Guid? requested, CancellationToken ct)
    {
        var mine = await sis.GetMyStudentAsync(ct);
        var staff = IsStaff(User);

        if (requested is { } sid)
        {
            var existing = await sis.GetStudentAsync(sid, ct);
            if (existing.IsSuccess)
            {
                if (staff || (mine.IsSuccess && mine.Data!.Id == sid))
                    return ApiResult<Guid?>.Ok(sid);
                if (mine.IsSuccess)
                    return ApiResult<Guid?>.Ok(mine.Data!.Id);
                return ApiResult<Guid?>.Ok(sid);
            }
        }

        if (mine.IsSuccess)
            return ApiResult<Guid?>.Ok(mine.Data!.Id);

        if (staff)
            return ApiResult<Guid?>.Ok(requested);

        return mine.Error is { } err
            ? ApiResult<Guid?>.Fail(err, mine.StatusCode)
            : ApiResult<Guid?>.Fail(new Error("not_found", "no linked student record"), 404);
    }

    private static bool IsStaff(ClaimsPrincipal user)
    {
        foreach (var claim in user.FindAll("role"))
        {
            var role = claim.Value.ToLowerInvariant();
            if (role.Contains("admin") || role.Contains("teacher") || role.Contains("principal")
                || role.Contains("owner") || role is "staff" || role.Contains("platform"))
                return true;
        }
        return false;
    }
}
