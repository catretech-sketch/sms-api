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

    [Fact]
    public void Silver_has_core_modules_not_geofence_or_hr()
    {
        var set = ForTier("silver");
        set.Has(FeatureCatalog.Sis).Should().BeTrue();
        set.Has(FeatureCatalog.Attendance).Should().BeTrue();
        set.Has(FeatureCatalog.Operations).Should().BeFalse();
        set.Has(FeatureCatalog.StaffSupport).Should().BeFalse();
        set.Has(FeatureCatalog.HrPayroll).Should().BeFalse();
        set.Has(FeatureCatalog.AttendanceGeofence).Should().BeFalse();
        set.Has(FeatureCatalog.TransportGps).Should().BeFalse();
    }

    [Fact]
    public void Gold_adds_analytics_not_hr_or_platinum()
    {
        var set = ForTier("gold");
        set.Has(FeatureCatalog.HrPayroll).Should().BeFalse();
        set.Has(FeatureCatalog.StaffSupport).Should().BeFalse();
        set.Has(FeatureCatalog.AnalyticsWeakStudents).Should().BeTrue();
        set.Has(FeatureCatalog.AttendanceGeofence).Should().BeFalse();
        set.Has(FeatureCatalog.TransportGps).Should().BeFalse();
    }

    [Fact]
    public void Platinum_adds_hr_geofence_gps_and_support()
    {
        var set = ForTier("platinum");
        set.Has(FeatureCatalog.HrPayroll).Should().BeTrue();
        set.Has(FeatureCatalog.StaffSupport).Should().BeTrue();
        set.Has(FeatureCatalog.Operations).Should().BeTrue();
        set.Has(FeatureCatalog.Transport).Should().BeTrue();
        set.Has(FeatureCatalog.AttendanceGeofence).Should().BeTrue();
        set.Has(FeatureCatalog.TransportGps).Should().BeTrue();
        set.Has(FeatureCatalog.SupportDedicated).Should().BeTrue();
        set.Has(FeatureCatalog.Sis).Should().BeTrue();
    }

    [Fact]
    public void Unknown_feature_key_is_not_granted()
    {
        ForTier("platinum").Has("does.not.exist").Should().BeFalse();
    }
}
