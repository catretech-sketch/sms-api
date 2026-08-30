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
    [InlineData("GreetById", "school.admin", true)]
    [InlineData("GreetById", "school.owner", true)]
    [InlineData("GreetById", "school.principal", true)]
    [InlineData("GreetById", "school.teacher", true)]
    [InlineData("GreetById", "staff", true)]
    [InlineData("GreetById", "student.parent", true)]
    [InlineData("GreetById", "some.unrelated.role", false)]
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

    [Theory]
    [InlineData("school.admin")]
    [InlineData("school.owner")]
    [InlineData("school.principal")]
    [InlineData("school.teacher")]
    [InlineData("staff")]
    [InlineData("student.parent")]
    public void PersonLookup_is_allowed_for_every_existing_role(string role)
    {
        AiIntentAccessRules.IsAllowed("PersonLookup", [role]).Should().BeTrue();
    }

    [Fact]
    public void MyTripStatus_is_allowed_only_for_driver()
    {
        AiIntentAccessRules.IsAllowed("MyTripStatus", ["driver"]).Should().BeTrue();
        AiIntentAccessRules.IsAllowed("MyTripStatus", ["school.admin"]).Should().BeFalse();
        AiIntentAccessRules.IsAllowed("MyTripStatus", ["school.teacher"]).Should().BeFalse();
        AiIntentAccessRules.IsAllowed("MyTripStatus", ["staff"]).Should().BeFalse();
        AiIntentAccessRules.IsAllowed("MyTripStatus", ["student.parent"]).Should().BeFalse();
    }

    [Theory]
    [InlineData("DailyAttendanceSummary")]
    [InlineData("ClassAttendance")]
    [InlineData("StudentAttendance")]
    [InlineData("TeacherAttendance")]
    [InlineData("StaffAttendance")]
    [InlineData("DashboardSummary")]
    [InlineData("StudentSearch")]
    [InlineData("StudentDetails")]
    [InlineData("TeacherSearch")]
    [InlineData("StaffSearch")]
    [InlineData("UpcomingExamSearch")]
    [InlineData("HomeworkSearch")]
    [InlineData("SubjectSearch")]
    [InlineData("BusLocationSearch")]
    [InlineData("GreetById")]
    [InlineData("PersonLookup")]
    public void Driver_is_denied_every_intent_except_MyTripStatus(string intent)
    {
        AiIntentAccessRules.IsAllowed(intent, ["driver"]).Should().BeFalse(
            "driver's AI surface is deliberately the smallest of any role - promoting it into Policies.All must not widen any existing intent");
    }
}
