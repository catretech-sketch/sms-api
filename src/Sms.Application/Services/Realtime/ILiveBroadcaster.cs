namespace Sms.Application.Services.Realtime;

public static class LiveGroups
{
    public static string Tenant(Guid tenantId) => $"tenant:{tenantId:D}";
    public static string User(Guid userId) => $"user:{userId:D}";
}

public static class LiveEventTypes
{
    public const string Attendance = "attendance";
    public const string Chat = "chat";
    public const string Announcement = "announcement";
    public const string Notification = "notification";
    public const string Homework = "homework";
    public const string Grades = "grades";
    public const string Exams = "exams";
    public const string Timetable = "timetable";
    public const string Fees = "fees";
    public const string Leave = "leave";
    public const string Transport = "transport";
}

public interface ILiveBroadcaster
{
    Task PublishAsync(Guid tenantId, string type, object? data = null, CancellationToken ct = default);
}

public sealed class NoOpLiveBroadcaster : ILiveBroadcaster
{
    public Task PublishAsync(Guid tenantId, string type, object? data = null, CancellationToken ct = default) =>
        Task.CompletedTask;
}
