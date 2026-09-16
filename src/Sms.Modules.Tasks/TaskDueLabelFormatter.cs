namespace Sms.Modules.Tasks;

/// <summary>
/// Pure, stateless formatting of a task's DueDate into the short label the staff app's
/// TaskDTO.due_label shows on a task row. Takes "today" explicitly (rather than reading
/// DateTime.Now/UtcNow itself) so it's trivially unit-testable and never timezone-flaky.
/// </summary>
public static class TaskDueLabelFormatter
{
    /// <summary>
    /// Scheme: no due date -> null (field omitted client-side); due date in the past -> "Overdue";
    /// due today -> "Today"; due tomorrow -> "Tomorrow"; anything further out -> a short formatted
    /// date, e.g. "Mon, 15 Sep". Comparison is by calendar date only (time-of-day is ignored).
    /// </summary>
    public static string? Format(DateTime? dueDate, DateTime today)
    {
        if (dueDate is not { } due) return null;

        var dueDay = due.Date;
        var todayDay = today.Date;
        var days = (dueDay - todayDay).Days;

        return days switch
        {
            < 0 => "Overdue",
            0 => "Today",
            1 => "Tomorrow",
            _ => dueDay.ToString("ddd, d MMM"),
        };
    }
}
