namespace Sms.Shared.Kernel.Authz;

/// Every known gateable feature key. RequiresFeature("x") must use a key listed here.
public static class FeatureCatalog
{
    public const string TransportGps = "transport.gps";
    public const string ExamsDatesheet = "exams.datesheet";
    public const string ReportsCsv = "reports.csv";
    public const string AnalyticsAdvanced = "analytics.advanced";
    public const string CommsTargeted = "comms.announcements.targeted";

    public static readonly string[] All =
        [TransportGps, ExamsDatesheet, ReportsCsv, AnalyticsAdvanced, CommsTargeted];
}
