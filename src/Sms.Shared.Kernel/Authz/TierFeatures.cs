namespace Sms.Shared.Kernel.Authz;

/// Tier → granted feature keys. Mirrors sms-admin FEATURE_TIER in mockDb.ts.
public static class TierFeatures
{
    private static readonly string[] Silver =
    [
        FeatureCatalog.Sis, FeatureCatalog.Academics, FeatureCatalog.Attendance, FeatureCatalog.Exams,
        FeatureCatalog.Fees, FeatureCatalog.Communication,
        FeatureCatalog.ExamsDatesheet, FeatureCatalog.CommsTargeted,
    ];

    private static readonly string[] Gold =
    [
        FeatureCatalog.AnalyticsWeakStudents, FeatureCatalog.ReportingAdvanced,
        FeatureCatalog.ReportsCsv, FeatureCatalog.AnalyticsAdvanced,
    ];

    private static readonly string[] Platinum =
    [
        FeatureCatalog.Operations, FeatureCatalog.Library, FeatureCatalog.Transport, FeatureCatalog.Hostel,
        FeatureCatalog.Sports, FeatureCatalog.HrPayroll, FeatureCatalog.StaffSupport,
        FeatureCatalog.AttendanceGeofence, FeatureCatalog.TransportGps, FeatureCatalog.SupportDedicated,
        FeatureCatalog.AiSearch,
    ];

    public static IReadOnlyCollection<string> For(string tier)
    {
        var rank = Rank(tier);
        var set = new HashSet<string>(Silver);
        if (rank >= 2) foreach (var f in Gold) set.Add(f);
        if (rank >= 3) foreach (var f in Platinum) set.Add(f);
        return set;
    }

    private static int Rank(string tier) => (tier ?? "").Trim().ToLowerInvariant() switch
    {
        "platinum" => 3,
        "gold" => 2,
        "silver" => 1,
        _ => 0,
    };
}
