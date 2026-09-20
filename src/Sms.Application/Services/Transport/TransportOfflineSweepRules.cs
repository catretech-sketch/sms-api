namespace Sms.Application.Services.Transport;

/// Pure "fire once until recovered" transition logic for the offline sweep:
/// a trip crossing the stale threshold is notified exactly once, and only
/// re-notified after it's seen fresh again (removed from currentlyStale)
/// and then goes stale a second time.
public static class TransportOfflineSweepRules
{
    public static (IReadOnlyList<Guid> ToNotify, IReadOnlyList<Guid> ToClear) ComputeTransitions(
        IReadOnlySet<Guid> previouslyOffline, IReadOnlySet<Guid> currentlyStale)
    {
        var toNotify = currentlyStale.Where(id => !previouslyOffline.Contains(id)).ToList();
        var toClear = previouslyOffline.Where(id => !currentlyStale.Contains(id)).ToList();
        return (toNotify, toClear);
    }
}
