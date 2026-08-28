using FluentAssertions;
using Sms.Application.Services.AiSearch;
using Xunit;

namespace Sms.Tests.Unit.AiSearch;

public class AiIntentAccessRulesTests
{
    [Theory]
    [InlineData("DailyAttendanceSummary", "school.admin", true)]
    [InlineData("DailyAttendanceSummary", "school.teacher", true)]
    [InlineData("DailyAttendanceSummary", "student.parent", false)]
    [InlineData("DashboardSummary", "school.principal", true)]
    [InlineData("DashboardSummary", "staff", false)]
    [InlineData("StudentAttendance", "student.parent", true)]
    [InlineData("BusLocationSearch", "student.parent", true)]
    [InlineData("BusLocationSearch", "school.teacher", false)]
    public void Role_matrix_matches_the_spec(string intent, string role, bool expected)
    {
        AiIntentAccessRules.IsAllowed(intent, [role]).Should().Be(expected);
    }

    [Fact]
    public void Unknown_intent_is_never_allowed()
    {
        AiIntentAccessRules.IsAllowed("DeleteEverything", ["school.admin"]).Should().BeFalse();
    }

    [Fact]
    public void Multiple_roles_are_allowed_if_any_role_matches()
    {
        AiIntentAccessRules.IsAllowed("DashboardSummary", ["staff", "school.admin"]).Should().BeTrue();
    }
}
