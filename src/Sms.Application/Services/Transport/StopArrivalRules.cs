namespace Sms.Application.Services.Transport;

/// Pure distance-vs-radius check for stop-arrival detection — the actual
/// Haversine distance computation lives in TripRepository (matching this
/// codebase's existing convention of a private per-class Haversine helper,
/// e.g. BusRepository.GetPositionAsync and AttendanceModule.PunchAsync each
/// have their own), this is just the boundary comparison, kept separate so
/// it's testable without a database.
public static class StopArrivalRules
{
    public static bool IsWithinRadius(double distanceMeters, double radiusMeters) =>
        distanceMeters <= radiusMeters;
}
