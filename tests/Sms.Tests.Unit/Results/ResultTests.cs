using FluentAssertions;
using Sms.Shared.Kernel.Results;
using Xunit;

namespace Sms.Tests.Unit.Results;

public class ResultTests
{
    [Fact]
    public void Ok_carries_value_and_is_success()
    {
        var r = Result<int>.Ok(42);
        r.IsSuccess.Should().BeTrue();
        r.Value.Should().Be(42);
        r.Error.Should().BeNull();
    }

    [Fact]
    public void Fail_carries_error_and_is_not_success()
    {
        var r = Result<int>.Fail(new Error("not_found", "missing"));
        r.IsSuccess.Should().BeFalse();
        r.Error!.Code.Should().Be("not_found");
        r.Error.Message.Should().Be("missing");
    }
}
