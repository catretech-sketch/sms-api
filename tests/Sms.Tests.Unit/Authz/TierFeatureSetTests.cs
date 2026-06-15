using FluentAssertions;
using Sms.Shared.Kernel.Authz;
using Sms.Shared.Kernel.Tenancy;
using Xunit;

namespace Sms.Tests.Unit.Authz;

public class TierFeatureSetTests
{
    private static ITenantFeatureSet ForTier(string tier)
    {
        var plan = new TenantPlan();
        plan.Set(Guid.NewGuid(), tier, "active");
        return new TierFeatureSet(plan);
    }

    [Theory]
    [InlineData("silver")]
    [InlineData("gold")]
    [InlineData("platinum")]
    [InlineData("")] // unknown tier
    public void All_tiers_grant_every_catalog_feature(string tier)
    {
        var set = ForTier(tier);
        foreach (var f in FeatureCatalog.All)
            set.Has(f).Should().BeTrue($"{tier} grants {f} (all-level policy)");
    }

    [Fact]
    public void Unknown_feature_key_is_not_granted()
    {
        ForTier("gold").Has("does.not.exist").Should().BeFalse();
    }
}
