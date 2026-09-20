using FluentAssertions;
using Sms.Modules.Transport;

namespace Sms.Tests.Unit.Transport;

public class BusParentAlertRulesTests
{
    [Fact]
    public void One_km_from_the_stop_is_in_range()
    {
        BusParentAlertRules.IsWithinApproach(1000).Should().BeTrue();
        BusParentAlertRules.IsWithinApproach(850).Should().BeTrue();
        BusParentAlertRules.IsWithinApproach(0).Should().BeTrue();
    }

    [Fact]
    public void Farther_than_one_km_is_not_in_range()
    {
        BusParentAlertRules.IsWithinApproach(1000.1).Should().BeFalse();
        BusParentAlertRules.IsWithinApproach(5000).Should().BeFalse();
    }
}
