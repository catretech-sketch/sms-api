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
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var result = await academics.GetHomeworkAsync(id, ct);
        if (result.Error is not null)
            return FromResult(result);
        if (!RoleChecks.IsStaff(User) && !await sis.IsLinkedToCallerAsync(result.Data!.StudentId, ct))
            return ForbiddenResult("not your linked student");
        return FromResult(result);
    }

    [HttpPost("homework")]
    public async Task<IActionResult> Create([FromBody] CreateHomeworkRequest req, CancellationToken ct)
    {
        if (!RoleChecks.IsStaff(User))
            return ForbiddenResult("staff only");
        return FromResult(await academics.CreateHomeworkAsync(req, ct));
    }

    [HttpPatch("homework/{id:guid}")]
    public async Task<IActionResult> SetStatus(
        Guid id, [FromBody] SetHomeworkStatusRequest req, CancellationToken ct)
    {
        if (await DenyHomeworkIfUnlinkedAsync(id, ct) is { } denied)
            return denied;
        return FromResult(await academics.SetHomeworkStatusAsync(id, req, ct));
    }

    [HttpPost("homework/{id:guid}/submit")]
    public async Task<IActionResult> Submit(Guid id, CancellationToken ct)
    {
        if (await DenyHomeworkIfUnlinkedAsync(id, ct) is { } denied)
            return denied;
        return FromResult(await academics.SubmitHomeworkAsync(id, ct));
    }

    /// <summary>
    /// Staff may pass a SIS id or omit it to list all. Students/parents may only
    /// resolve a roster row they are linked to (ParentStudentLinks or self).
    /// </summary>
    private async Task<ApiResult<Guid?>> ResolveStudentIdAsync(Guid? requested, CancellationToken ct)
    {
        if (RoleChecks.IsStaff(User))
            return ApiResult<Guid?>.Ok(requested);

        if (requested is { } sid)
        {
            if (await sis.IsLinkedToCallerAsync(sid, ct))
                return ApiResult<Guid?>.Ok(sid);
            return ApiResult<Guid?>.Fail(
                new Error("forbidden", "not your linked student"), 403);
        }

        var mine = await sis.GetMyStudentAsync(ct);
        if (mine.IsSuccess)
            return ApiResult<Guid?>.Ok(mine.Data!.Id);

        var kids = await sis.ListMyChildrenAsync(ct);
        if (kids.IsSuccess && kids.Data is { Count: 1 })
            return ApiResult<Guid?>.Ok(kids.Data[0].Id);
        if (kids.IsSuccess && kids.Data is { Count: > 1 })
            return ApiResult<Guid?>.Fail(new Error("student_id_required", "student_id is required"), 400);

        return mine.Error is { } err
            ? ApiResult<Guid?>.Fail(err, mine.StatusCode)
            : ApiResult<Guid?>.Fail(new Error("not_found", "no linked student record"), 404);
    }

    private async Task<IActionResult?> DenyHomeworkIfUnlinkedAsync(Guid homeworkId, CancellationToken ct)
    {
        if (RoleChecks.IsStaff(User)) return null;
        var hw = await academics.GetHomeworkAsync(homeworkId, ct);
        if (hw.Error is not null)
            return FromResult(hw);
        if (!await sis.IsLinkedToCallerAsync(hw.Data!.StudentId, ct))
            return ForbiddenResult("not your linked student");
        return null;
    }
}
