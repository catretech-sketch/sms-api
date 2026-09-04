using Sms.Application.Services.Transport;
using Xunit;
using FluentAssertions;

namespace Sms.Tests.Unit.Transport;

public class StopArrivalRulesTests
{
    [Fact]
    public void Within_radius_is_true_when_distance_is_less_than_radius()
    {
        StopArrivalRules.IsWithinRadius(distanceMeters: 40, radiusMeters: 100).Should().BeTrue();
    }

    [Fact]
    public void Within_radius_is_false_when_distance_exceeds_radius()
    {
        StopArrivalRules.IsWithinRadius(distanceMeters: 150, radiusMeters: 100).Should().BeFalse();
    }

    [Fact]
    public void Within_radius_is_true_at_exactly_the_boundary()
    {
        StopArrivalRules.IsWithinRadius(distanceMeters: 100, radiusMeters: 100).Should().BeTrue();
    }
}
