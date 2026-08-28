using FluentAssertions;
using Sms.Shared.Kernel.AiSearch;
using Xunit;

namespace Sms.Tests.Unit.AiSearch;

public class AiSearchOptionsTests
{
    [Fact]
    public void Defaults_are_sane_and_IsConfigured_requires_an_api_key()
    {
        var opts = new AiSearchOptions();

        opts.IsConfigured.Should().BeFalse();
        opts.MaxQueryLength.Should().Be(300);
        opts.TimeoutSeconds.Should().Be(8);

        opts.ApiKey = "sk-test";
        opts.IsConfigured.Should().BeTrue();
    }
}
