namespace Sms.Shared.Kernel.Authz;

public static class RequireFeature
{
    public const string LockedCode = "feature_locked";
    public static bool IsAllowed(ITenantFeatureSet features, string feature) => features.Has(feature);
}

/// Endpoint filter marker — wired in Program.cs via AddEndpointFilter on grouped routes.
[AttributeUsage(AttributeTargets.Method)]
public sealed class RequiresFeatureAttribute(string feature) : Attribute
{
    public string Feature { get; } = feature;
}
