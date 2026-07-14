using Sms.Shared.Kernel.Results;

namespace Sms.Application.Common;

/// Outcome from a service method — controllers map this to HTTP responses.
public sealed record ApiResult<T>(T? Data, Error? Error, int StatusCode = 200)
{
    public bool IsSuccess => Error is null;

    public static ApiResult<T> Ok(T data, int statusCode = 200) => new(data, null, statusCode);
    public static ApiResult<T> Fail(Error error, int statusCode) => new(default, error, statusCode);
    public static ApiResult<T> NoContent() => new(default, null, 204);
}

public sealed record ApiResult(Error? Error, int StatusCode = 200)
{
    public bool IsSuccess => Error is null;

    public static ApiResult Ok(int statusCode = 200) => new(null, statusCode);
    public static ApiResult Fail(Error error, int statusCode) => new(error, statusCode);
    public static ApiResult NoContent() => new(null, 204);
}
