using Sms.Shared.Kernel.Tenancy;

namespace Sms.Shared.Kernel.Authz;

public sealed class TierFeatureSet(ITenantPlan plan) : ITenantFeatureSet
{
    public bool Has(string feature) => TierFeatures.For(plan.Tier).Contains(feature);
}
