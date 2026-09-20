namespace Sms.Application.Services.Transport;

/// Decides which role (driver or conductor) is treated as the trip's active GPS broadcaster,
/// purely from ping recency — driver preferred. This is display/decision-side only: the server
/// never rejects a ping based on this (see the design spec's "Why accept-always" section);
/// this result is what the conductor's app uses to decide whether to run its own background
/// broadcast, and what fleet/parent views use to show "who is currently sharing location."
public static class TripBroadcasterRules
{
    public static readonly TimeSpan StaleAfter = TimeSpan.FromSeconds(30);

    public static string? Compute(DateTime? driverLastPingAt, DateTime? conductorLastPingAt, DateTime now)
    {
        if (driverLastPingAt is { } d && now - d < StaleAfter) return "driver";
        if (conductorLastPingAt is { } c && now - c < StaleAfter) return "conductor";
        return null;
    }
}
