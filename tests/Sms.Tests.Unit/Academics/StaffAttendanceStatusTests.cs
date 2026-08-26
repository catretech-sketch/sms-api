using FluentAssertions;
using Sms.Modules.Academics;
using Xunit;

namespace Sms.Tests.Unit.Academics;

public class StaffAttendanceStatusTests
{
    [Theory]
    [InlineData("present", "present")]
    [InlineData("ABSENT", "absent")]
    [InlineData("late", "late")]
    [InlineData("half_day", "half_day")]
    [InlineData("half-day", "half_day")]
    [InlineData(" Half Day ", null)]
    [InlineData("leave", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void Normalize_allows_present_absent_late_half_day(string? raw, string? expected)
    {
        StaffAttendanceStatus.Normalize(raw).Should().Be(expected);
    }

    [Theory]
    [InlineData("present", true)]
    [InlineData("late", true)]
    [InlineData("half_day", true)]
    [InlineData("absent", false)]
    [InlineData("leave", false)]
    public void Half_day_counts_as_on_campus_like_present(string status, bool onCampus)
    {
        StaffAttendanceStatus.IsOnCampus(status).Should().Be(onCampus);
    }
}
