using FluentAssertions;
using Sms.Application.Services.Academics;
using Xunit;

namespace Sms.Tests.Unit.Academics;

public class AttendanceRollCallTests
{
    [Fact]
    public void First_teaching_period_skips_assembly_and_lunch()
    {
        var slots = new[]
        {
            Slot(1, "Assembly"),
            Slot(2, "Mathematics"),
            Slot(4, "Lunch"),
            Slot(5, "English"),
        };
        var got = AttendanceRollCall.FirstTeachingSlot(slots);
        got!.Period.Should().Be(2);
        got.Subject.Should().Be("Mathematics");
    }

    [Fact]
    public void Weekday_is_invariant_english_three_letter()
    {
        AttendanceRollCall.DayKey(new DateTime(2026, 8, 12)).Should().Be("Wed");
        AttendanceRollCall.DayKey(new DateTime(2026, 8, 15)).Should().Be("Sat");
    }

    [Fact]
    public void Subject_teacher_cannot_mark_when_not_p1_or_class_teacher()
    {
        AttendanceRollCall.CanMark(
            isLeadership: false,
            callerTeacherId: Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            classTeacherId: Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            rollCallTeacherId: Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"))
            .Should().BeFalse();
    }

    [Fact]
    public void Class_teacher_and_p1_teacher_and_leadership_can_mark()
    {
        var classT = Guid.NewGuid();
        var p1 = Guid.NewGuid();
        AttendanceRollCall.CanMark(false, classT, classT, p1).Should().BeTrue();
        AttendanceRollCall.CanMark(false, p1, classT, p1).Should().BeTrue();
        AttendanceRollCall.CanMark(true, Guid.NewGuid(), classT, p1).Should().BeTrue();
    }

    [Fact]
    public void Resolve_picks_first_teaching_slot_for_requested_weekday()
    {
        var slots = new[]
        {
            Slot("Mon", 1, "Assembly"),
            Slot("Mon", 2, "Mathematics"),
            Slot("Wed", 1, "Assembly"),
            Slot("Wed", 3, "Science"),
        };
        var got = AttendanceRollCall.Resolve(slots, new DateTime(2026, 8, 12));
        got!.Day.Should().Be("Wed");
        got.Period.Should().Be(3);
        got.Subject.Should().Be("Science");
    }

    static AttendanceRollCall.SlotInput Slot(int period, string subject) =>
        new("Wed", period, subject, Guid.Empty, null);

    static AttendanceRollCall.SlotInput Slot(string day, int period, string subject) =>
        new(day, period, subject, Guid.Empty, null);
}
