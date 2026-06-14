using FluentAssertions;
using Sms.Shared.Kernel.Authz;
using Xunit;

namespace Sms.Tests.Unit.Authz;

public class RequireFeatureTests
{
    private sealed class FakeFeatures(params string[] on) : ITenantFeatureSet
    {
        private readonly HashSet<string> _on = new(on);
        public bool Has(string feature) => _on.Contains(feature);
    }

    [Fact]
    public void Allows_when_feature_present()
    {
        var f = new FakeFeatures("attendance.geofence");
        RequireFeature.IsAllowed(f, "attendance.geofence").Should().BeTrue();
    }

    [Fact]
    public void Denies_with_feature_locked_code_when_absent()
    {
        var f = new FakeFeatures();
        RequireFeature.IsAllowed(f, "attendance.geofence").Should().BeFalse();
        RequireFeature.LockedCode.Should().Be("feature_locked");
    }
}
