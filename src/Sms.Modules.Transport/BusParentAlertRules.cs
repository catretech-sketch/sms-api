namespace Sms.Modules.Transport;

/// Parent live-bus alerts: a trip that has started, or a bus entering the student's stop radius.
public static class BusParentAlertRules
{
    public const string TripStarted = "trip_started";
    public const string ApproachingStop = "approaching_stop";

    /// Default: notify when the bus is within 1 km of the child's assigned stop.
    public const double DefaultApproachMeters = 1000;

    public static bool IsWithinApproach(double distanceMeters, double radiusMeters = DefaultApproachMeters) =>
        distanceMeters <= radiusMeters && distanceMeters >= 0;
}
