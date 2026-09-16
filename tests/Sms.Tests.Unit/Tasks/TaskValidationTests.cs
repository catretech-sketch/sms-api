using Sms.Application.Services.Tasks;
using Sms.Modules.Tasks;
using Xunit;

namespace Sms.Tests.Unit.Tasks;

public class TaskValidationTests
{
    [Theory]
    [InlineData("urgent", true)]
    [InlineData("normal", true)]
    [InlineData("high", false)]
    public void Priority_validation_accepts_only_the_two_approved_values(string priority, bool expected) =>
        Assert.Equal(expected, TaskEnums.ValidPriorities.Contains(priority));

    [Theory]
    [InlineData("pending", true)]
    [InlineData("in_progress", true)]
    [InlineData("completed", true)]
    [InlineData("open", false)]
    public void Status_validation_accepts_only_the_three_approved_values(string status, bool expected) =>
        Assert.Equal(expected, TaskEnums.ValidStatuses.Contains(status));

    [Theory]
    [InlineData("driver", true)]
    [InlineData("conductor", true)]
    [InlineData("sweeper", true)]
    [InlineData("gardener", true)]
    [InlineData("guard", true)]
    [InlineData("peon", true)]
    [InlineData("teacher", false)]
    public void RoleKey_validation_accepts_only_the_six_duty_roles(string roleKey, bool expected) =>
        Assert.Equal(expected, TaskEnums.ValidRoleKeys.Contains(roleKey));

    [Fact]
    public void Exactly_one_assignment_target_rejects_both_set()
    {
        Assert.False(TaskService.IsExactlyOneAssignmentTarget(Guid.NewGuid(), "driver"));
    }

    [Fact]
    public void Exactly_one_assignment_target_rejects_neither_set()
    {
        Assert.False(TaskService.IsExactlyOneAssignmentTarget(null, null));
        Assert.False(TaskService.IsExactlyOneAssignmentTarget(null, ""));
        Assert.False(TaskService.IsExactlyOneAssignmentTarget(null, "  "));
    }

    [Fact]
    public void Exactly_one_assignment_target_accepts_user_only()
    {
        Assert.True(TaskService.IsExactlyOneAssignmentTarget(Guid.NewGuid(), null));
    }

    [Fact]
    public void Exactly_one_assignment_target_accepts_role_only()
    {
        Assert.True(TaskService.IsExactlyOneAssignmentTarget(null, "driver"));
    }

    [Fact]
    public void Due_label_is_null_when_no_due_date()
    {
        Assert.Null(TaskDueLabelFormatter.Format(null, new DateTime(2026, 9, 16)));
    }

    [Fact]
    public void Due_label_is_today_for_the_current_date()
    {
        var today = new DateTime(2026, 9, 16);
        Assert.Equal("Today", TaskDueLabelFormatter.Format(today, today));
    }

    [Fact]
    public void Due_label_is_tomorrow_for_the_next_date()
    {
        var today = new DateTime(2026, 9, 16);
        Assert.Equal("Tomorrow", TaskDueLabelFormatter.Format(today.AddDays(1), today));
    }

    [Fact]
    public void Due_label_is_overdue_for_a_past_date()
    {
        var today = new DateTime(2026, 9, 16);
        Assert.Equal("Overdue", TaskDueLabelFormatter.Format(today.AddDays(-1), today));
    }

    [Fact]
    public void Due_label_is_a_short_formatted_date_further_out()
    {
        var today = new DateTime(2026, 9, 16);
        Assert.Equal("Fri, 25 Sep", TaskDueLabelFormatter.Format(new DateTime(2026, 9, 25), today));
    }

    [Fact]
    public void Due_label_ignores_time_of_day_when_comparing_dates()
    {
        var today = new DateTime(2026, 9, 16, 23, 59, 0);
        var dueDate = new DateTime(2026, 9, 16, 0, 0, 0);
        Assert.Equal("Today", TaskDueLabelFormatter.Format(dueDate, today));
    }
}
