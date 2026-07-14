using Microsoft.AspNetCore.Mvc;
using Sms.Application.Common;
using Sms.Shared.Kernel.Http;
using Sms.Shared.Kernel.Results;

namespace Sms.Api.Controllers;

[ApiController]
public abstract class ApiControllerBase : ControllerBase
{
    protected IActionResult FromResult<T>(ApiResult<T> result)
    {
        if (result.Error is { } error)
            return StatusCode(result.StatusCode, ErrorEnvelope.From(error));
        return result.StatusCode switch
        {
            201 => StatusCode(201, new DataEnvelope<T>(result.Data!)),
            204 => NoContent(),
            _ => Ok(new DataEnvelope<T>(result.Data!))
        };
    }

    protected IActionResult FromResult(ApiResult result)
    {
        if (result.Error is { } error)
            return StatusCode(result.StatusCode, ErrorEnvelope.From(error));
        return result.StatusCode == 204 ? NoContent() : Ok();
    }

    protected IActionResult OkData<T>(T data) => Ok(new DataEnvelope<T>(data));
    protected IActionResult CreatedData<T>(T data) => StatusCode(201, new DataEnvelope<T>(data));
    protected IActionResult CursorOk<T>(IReadOnlyList<T> data, string? nextCursor = null) =>
        Ok(new CursorPage<T>(data, nextCursor));

    protected IActionResult FromCursorResult<T>(ApiResult<CursorPage<T>> result)
    {
        if (result.Error is { } error)
            return StatusCode(result.StatusCode, ErrorEnvelope.From(error));
        return CursorOk(result.Data!.Data, result.Data.NextCursor);
    }
    protected IActionResult ErrorResult(Error error, int statusCode) =>
        StatusCode(statusCode, ErrorEnvelope.From(error));
    protected IActionResult NotFoundResult() =>
        ErrorResult(new Error("not_found", "resource not found"), 404);
    protected IActionResult ForbiddenResult(string message) =>
        ErrorResult(new Error("forbidden", message), 403);
    protected IActionResult ConflictResult(string message) =>
        ErrorResult(new Error("conflict", message), 409);
    protected IActionResult BadRequestResult(string message) =>
        ErrorResult(new Error("bad_request", message), 400);
}
