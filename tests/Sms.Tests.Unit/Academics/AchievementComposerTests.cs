using FluentAssertions;
using Sms.Modules.Academics;
using Sms.Modules.Academics.Contracts;

namespace Sms.Tests.Unit.Academics;

public class AchievementComposerTests
{
    private static readonly Guid Student = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
    private static readonly DateTime AsOf = new(2026, 8, 13, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Perfect_attendance_is_100_percent_only()
    {
        var perfect = AchievementComposer.Compose(Student, 100m, [], [], [], AsOf);
        perfect.Should().ContainSingle(a => a.Title == "Perfect attendance");
        perfect.Should().NotContain(a => a.Title == "Excellent attendance");

        var excellent = AchievementComposer.Compose(Student, 96.5m, [], [], [], AsOf);
        excellent.Should().ContainSingle(a => a.Title == "Excellent attendance");
        excellent.Should().NotContain(a => a.Title == "Perfect attendance");
    }

    [Fact]
    public void No_attendance_badge_when_unmarked()
    {
        AchievementComposer.Compose(Student, null, [], [], [], AsOf).Should().BeEmpty();
    }

    [Fact]
    public void Homework_complete_only_when_every_item_is_done()
    {
        var allDone = AchievementComposer.Compose(
            Student, null, ["submitted", "graded"], [], [], AsOf);
        allDone.Should().ContainSingle(a => a.Title == "All homework submitted");

        var open = AchievementComposer.Compose(
            Student, null, ["todo", "submitted"], [], [], AsOf);
        open.Should().BeEmpty();

        AchievementComposer.Compose(Student, null, [], [], [], AsOf).Should().BeEmpty();
    }

    [Fact]
    public void Academic_badges_use_published_mark_average()
    {
        var distinction = AchievementComposer.Compose(
            Student, null, [], [(90, 100), (95, 100)], [], AsOf);
        distinction.Should().ContainSingle(a => a.Title == "Academic distinction");

        var honor = AchievementComposer.Compose(
            Student, null, [], [(80, 100), (82, 100)], [], AsOf);
        honor.Should().ContainSingle(a => a.Title == "Honor roll");
        honor.Should().NotContain(a => a.Title == "Academic distinction");
    }

    [Fact]
    public void Staff_awards_are_merged_and_sorted_by_date()
    {
        var award = new AchievementResponse(
            Guid.NewGuid().ToString(), "Math Olympiad — Silver", new DateTime(2026, 3, 1), "award", "yellow");
        var list = AchievementComposer.Compose(Student, 100m, [], [], [award], AsOf);
        list.Select(a => a.Title).Should().Equal("Perfect attendance", "Math Olympiad — Silver");
        list[0].Id.Should().StartWith("computed:");
    }
}
