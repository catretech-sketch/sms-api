using Sms.Application.Services.Transport;
using Xunit;
using FluentAssertions;

namespace Sms.Tests.Unit.Transport;

public class TransportOfflineSweepRulesTests
{
    [Fact]
    public void A_newly_stale_trip_not_previously_offline_should_be_notified()
    {
        var tripId = Guid.NewGuid();
        var (toNotify, toClear) = TransportOfflineSweepRules.ComputeTransitions(
            previouslyOffline: new HashSet<Guid>(),
            currentlyStale: new HashSet<Guid> { tripId });

        toNotify.Should().ContainSingle().Which.Should().Be(tripId);
        toClear.Should().BeEmpty();
    }

    [Fact]
    public void A_trip_still_stale_from_last_sweep_should_not_be_notified_again()
    {
        var tripId = Guid.NewGuid();
        var (toNotify, toClear) = TransportOfflineSweepRules.ComputeTransitions(
            previouslyOffline: new HashSet<Guid> { tripId },
            currentlyStale: new HashSet<Guid> { tripId });

        toNotify.Should().BeEmpty();
        toClear.Should().BeEmpty();
    }

    [Fact]
    public void A_trip_that_recovered_should_be_cleared_so_it_can_be_notified_again_later()
    {
        var tripId = Guid.NewGuid();
        var (toNotify, toClear) = TransportOfflineSweepRules.ComputeTransitions(
            previouslyOffline: new HashSet<Guid> { tripId },
            currentlyStale: new HashSet<Guid>());

        toNotify.Should().BeEmpty();
        toClear.Should().ContainSingle().Which.Should().Be(tripId);
    }

    [Fact]
    public void Unrelated_trips_are_independent()
    {
        var stillStale = Guid.NewGuid();
        var recovered = Guid.NewGuid();
        var newlyStale = Guid.NewGuid();
        var (toNotify, toClear) = TransportOfflineSweepRules.ComputeTransitions(
            previouslyOffline: new HashSet<Guid> { stillStale, recovered },
            currentlyStale: new HashSet<Guid> { stillStale, newlyStale });

        toNotify.Should().ContainSingle().Which.Should().Be(newlyStale);
        toClear.Should().ContainSingle().Which.Should().Be(recovered);
    }
}
