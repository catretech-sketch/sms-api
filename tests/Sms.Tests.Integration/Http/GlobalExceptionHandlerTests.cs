using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Sms.Api.Http;

namespace Sms.Tests.Integration.Http;

public class GlobalExceptionHandlerTests
{
    [Fact]
    public async Task Writes_snake_case_internal_error_envelope_with_500_and_no_leak()
    {
        var handler = new GlobalExceptionHandler(NullLogger<GlobalExceptionHandler>.Instance);
        var ctx = new DefaultHttpContext { Response = { Body = new MemoryStream() } };

        var handled = await handler.TryHandleAsync(ctx, new InvalidOperationException("boom secret detail"), default);

        handled.Should().BeTrue();
        ctx.Response.StatusCode.Should().Be(500);

        ctx.Response.Body.Seek(0, SeekOrigin.Begin);
        var body = await new StreamReader(ctx.Response.Body).ReadToEndAsync();
        body.Should().Contain("\"code\":\"internal_error\"");
        body.Should().NotContain("boom secret detail");
    }
}
