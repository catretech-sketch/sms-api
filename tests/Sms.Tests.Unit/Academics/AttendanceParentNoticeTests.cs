using FluentAssertions;
using Sms.Application.Services.Attendance;

namespace Sms.Tests.Unit.Academics;

public class AttendanceParentNoticeTests
{
    private static readonly Guid Ankit = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Maya = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public void AbsentStudentIds_includes_only_absent_marks()
    {
        var ids = AttendanceParentNotice.AbsentStudentIds([
            (Ankit, "present"),
            (Maya, "absent"),
            (Ankit, "ABSENT"),
            (Maya, "late"),
        ]);

        ids.Should().BeEquivalentTo([Maya, Ankit]);
    }

    [Fact]
    public void AbsentStudentIds_skips_present_corrections()
    {
        AttendanceParentNotice.AbsentStudentIds([
            (Ankit, "present"),
            (Maya, "late"),
            (Ankit, "leave"),
        ]).Should().BeEmpty();
    }

    [Fact]
    public void Title_and_body_name_the_child_and_period()
    {
        AttendanceParentNotice.Title("Ankit Rana").Should().Be("Ankit Rana marked absent");
        AttendanceParentNotice.Body(new DateTime(2026, 9, 18), "Science", 2)
            .Should().Be("Science · period 2 · 2026-09-18");
        AttendanceParentNotice.Body(new DateTime(2026, 9, 18))
            .Should().Be("2026-09-18");
    }
}
