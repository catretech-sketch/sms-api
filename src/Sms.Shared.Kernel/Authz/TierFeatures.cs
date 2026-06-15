namespace Sms.Shared.Kernel.Authz;

/// tier -> granted feature keys. Decision (2026-06-15, "all level"): ALL tiers grant the full
/// catalog — nothing is locked yet. To restrict a tier later, return a subset here; no endpoint
/// changes needed because RequiresFeature already enforces this map.
public static class TierFeatures
{
    public static IReadOnlyCollection<string> For(string tier) => FeatureCatalog.All;
}
