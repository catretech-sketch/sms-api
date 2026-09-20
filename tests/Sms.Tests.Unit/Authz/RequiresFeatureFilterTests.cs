using FluentAssertions;
using Sms.Shared.Kernel.Authz;
using Xunit;

namespace Sms.Tests.Unit.Authz;

public class RequiresFeatureFilterTests
{
    private sealed class StubSet(bool has) : ITenantFeatureSet
    {
        public bool Has(string feature) => has;
    }

    [Fact]
    public void Locked_returns_403_feature_locked()
    {
        RequiresFeatureFilter.Evaluate(new StubSet(false), "transport.gps", isPlatform: false)
            .Should().Be(403);
    }

    [Fact]
    public void Allowed_returns_zero()
    {
        RequiresFeatureFilter.Evaluate(new StubSet(true), "transport.gps", isPlatform: false)
            .Should().Be(0);
    }

    [Fact]
    public void Platform_bypasses()
    {
        RequiresFeatureFilter.Evaluate(new StubSet(false), "transport.gps", isPlatform: true)
            .Should().Be(0);
    }
}
