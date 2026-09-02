using Sms.Application.Services.Transport;

namespace Sms.Tests.Unit.Transport;

public class TripBroadcasterRulesTests
{
    private static readonly DateTime Now = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Driver_wins_when_only_driver_has_pinged_recently()
    {
        var result = TripBroadcasterRules.Compute(Now.AddSeconds(-5), null, Now);
        Assert.Equal("driver", result);
    }

    [Fact]
    public void Conductor_wins_when_only_conductor_has_pinged_recently()
    {
        var result = TripBroadcasterRules.Compute(null, Now.AddSeconds(-5), Now);
        Assert.Equal("conductor", result);
    }

    [Fact]
    public void Driver_is_preferred_when_both_have_pinged_recently()
    {
        var result = TripBroadcasterRules.Compute(Now.AddSeconds(-5), Now.AddSeconds(-1), Now);
        Assert.Equal("driver", result);
    }

    [Fact]
    public void Conductor_takes_over_once_the_drivers_ping_goes_stale()
    {
        var result = TripBroadcasterRules.Compute(Now.AddSeconds(-31), Now.AddSeconds(-5), Now);
        Assert.Equal("conductor", result);
    }

    [Fact]
    public void Returns_null_when_neither_has_pinged_yet()
    {
        var result = TripBroadcasterRules.Compute(null, null, Now);
        Assert.Null(result);
    }

    [Fact]
    public void Returns_null_when_both_are_stale()
    {
        var result = TripBroadcasterRules.Compute(Now.AddSeconds(-40), Now.AddSeconds(-35), Now);
        Assert.Null(result);
    }
}
