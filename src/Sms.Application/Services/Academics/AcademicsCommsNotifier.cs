using Sms.Modules.Comms;

namespace Sms.Application.Services.Academics;

/// <summary>
/// In-app notices for academics events (homework, class tests, timetable publish).
/// Uses the tenant-scoped <c>Notifications</c> feed — student, parent, teacher, and school CRM apps
/// all read <c>GET /v1/notifications</c>. Exam-term publish from admin is handled separately.
/// </summary>
public sealed class AcademicsCommsNotifier(CommsRepository comms)
{
    public async Task NotifyHomeworkAssignedAsync(
        Guid tenantId,
        string title,
        string? className,
        DateTime? dueDate,
        CancellationToken ct = default)
    {
        var trimmed = title.Trim();
        if (trimmed.Length == 0) return;

        var cls = string.IsNullOrWhiteSpace(className) ? "Class" : className.Trim();
        var due = dueDate is { } d ? d.ToString("yyyy-MM-dd") : null;
        var body = due is null ? cls : $"{cls} · due {due}";

        await comms.CreateNotificationAsync(tenantId, new CreateNotificationRequest(
            Icon: "book",
            Tone: "brand",
            Title: $"Homework: {trimmed}",
            Body: body), ct);
    }

    public async Task NotifyClassTestScheduledAsync(
        Guid tenantId,
        string? title,
        string? subject,
        DateTime? date,
        CancellationToken ct = default)
    {
        var name = (title ?? "").Trim();
        if (name.Length == 0) name = "Class test";

        var subj = string.IsNullOrWhiteSpace(subject) ? null : subject.Trim();
        var when = date is { } d ? d.ToString("yyyy-MM-dd") : null;
        var body = subj is null
            ? when ?? "Scheduled in CRM"
            : when is null ? subj : $"{subj} · {when}";

        await comms.CreateNotificationAsync(tenantId, new CreateNotificationRequest(
            Icon: "document-text",
            Tone: "brand",
            Title: $"Class test: {name}",
            Body: body), ct);
    }

    public async Task NotifyTimetablePublishedAsync(
        Guid tenantId,
        int classCount,
        int slotCount,
        CancellationToken ct = default)
    {
        if (classCount <= 0 || slotCount <= 0) return;

        var clsLabel = classCount == 1 ? "1 class" : $"{classCount} classes";
        var slotLabel = slotCount == 1 ? "1 period" : $"{slotCount} periods";
        await comms.CreateNotificationAsync(tenantId, new CreateNotificationRequest(
            Icon: "calendar",
            Tone: "brand",
            Title: "Timetable updated",
            Body: $"{clsLabel} · {slotLabel} with bell times — open Schedule to refresh."), ct);
    }
}
