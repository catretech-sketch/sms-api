namespace Sms.Shared.Kernel.Authz;

/// Small helper kept for the unit-tested allow/deny check. Endpoint enforcement is done by
/// RequiresFeatureFilter via the `.RequiresFeature("key")` route helper.
public static class RequireFeature
{
    public const string LockedCode = "feature_locked";
    public static bool IsAllowed(ITenantFeatureSet features, string feature) => features.Has(feature);
}
