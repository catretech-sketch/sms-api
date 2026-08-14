using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sms.Application.Common;
using Sms.Application.Services.Academics;
using Sms.Application.Services.Sis;
using Sms.Modules.Academics.Contracts;
using Sms.Shared.Kernel.Authz;
using Sms.Shared.Kernel.Results;

namespace Sms.Api.Controllers;

[Route("v1")]
[Authorize]
public sealed class AchievementController(IAcademicsService academics, ISisService sis) : ApiControllerBase
{
    [HttpGet("achievements")]
    public async Task<IActionResult> List(
        [FromQuery(Name = "student_id")] Guid? studentId, CancellationToken ct)
    {
        var resolved = await ResolveStudentIdAsync(studentId, ct);
        if (resolved.Error is not null)
            return FromResult(resolved);
        if (resolved.Data is not { } sid)
            return FromResult(ApiResult<IReadOnlyList<AchievementResponse>>.Fail(
                new Error("not_found", "no linked student record"), 404));
        return FromResult(await academics.ListAchievementsAsync(sid, ct));
    }

    [HttpPost("achievements")]
    [Authorize(Policy = AuthorizationPolicies.TeacherApp)]
    public async Task<IActionResult> Create([FromBody] CreateAchievementRequest req, CancellationToken ct) =>
        FromResult(await academics.CreateAchievementAsync(req, ct));

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
            return ApiResult<Guid?>.Fail(new Error("validation", "student_id is required"), 400);

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
