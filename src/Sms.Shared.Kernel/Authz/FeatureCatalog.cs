namespace Sms.Shared.Kernel.Authz;

/// Every known gateable feature key. RequiresFeature("x") must use a key listed here.
public static class FeatureCatalog
{
    // Platinum
    public const string AttendanceGeofence = "attendance.geofence";
    public const string TransportGps = "transport.gps";
    public const string SupportDedicated = "support.dedicated";

    // Gold
    public const string HrPayroll = "hr_payroll";
    public const string StaffSupport = "staff_support";
    public const string AnalyticsWeakStudents = "analytics.weak_students";
    public const string ReportingAdvanced = "reporting.advanced";

    // Legacy / module-specific keys (kept for existing gates)
    public const string ExamsDatesheet = "exams.datesheet";
    public const string ReportsCsv = "reports.csv";
    public const string AnalyticsAdvanced = "analytics.advanced";
    public const string CommsTargeted = "comms.announcements.targeted";

    // Silver core modules (aligned with sms-admin FEATURE_TIER)
    public const string Sis = "sis";
    public const string Academics = "academics";
    public const string Attendance = "attendance";
    public const string Exams = "exams";
    public const string Fees = "fees";
    public const string Communication = "communication";
    public const string Operations = "operations";
    public const string Library = "library";
    public const string Transport = "transport";
    public const string Hostel = "hostel";
    public const string Sports = "sports";

    public static readonly string[] All =
    [
        Sis, Academics, Attendance, Exams, Fees, Communication, Operations, Library, Transport, Hostel, Sports,
        ExamsDatesheet, ReportsCsv, AnalyticsAdvanced, CommsTargeted,
        HrPayroll, StaffSupport, AnalyticsWeakStudents, ReportingAdvanced,
        AttendanceGeofence, TransportGps, SupportDedicated,
    ];
}
