using FluentAssertions;
using Sms.Application.Services.Auth;
using Xunit;

namespace Sms.Tests.Unit.Auth;

public class AppLoginRoleTests
{
    [Fact]
    public void Student_tab_rejects_parent_accounts()
    {
        AppLoginRole.Matches(["student.parent"], "student").Should().BeFalse();
        AppLoginRole.Matches(["parent"], "student").Should().BeFalse();
    }

    [Fact]
    public void Student_tab_accepts_student_accounts()
    {
        AppLoginRole.Matches(["student"], "student").Should().BeTrue();
        AppLoginRole.Matches(["school.student"], "student").Should().BeTrue();
    }

    [Fact]
    public void Parent_tab_accepts_parent_and_rejects_student()
    {
        AppLoginRole.Matches(["student.parent"], "parent").Should().BeTrue();
        AppLoginRole.Matches(["student"], "parent").Should().BeFalse();
    }

    [Fact]
    public void Unspecified_role_does_not_filter()
    {
        AppLoginRole.Matches(["student.parent"], null).Should().BeTrue();
        AppLoginRole.Matches(["student"], "").Should().BeTrue();
    }

    [Fact]
    public void Student_parent_is_not_a_student_account()
    {
        AppLoginRole.IsStudent(["student.parent"]).Should().BeFalse();
        AppLoginRole.IsParent(["student.parent"]).Should().BeTrue();
    }

    [Fact]
    public void Wrong_tab_tells_parent_to_switch()
    {
        AppLoginRole.WrongTabMessage(["student.parent"], "student")
            .Should().Be("This is a parent login. Switch to the Parent tab.");
        AppLoginRole.WrongTabMessage(["student"], "parent")
            .Should().Be("This is a student login. Switch to the Student tab.");
        AppLoginRole.WrongTabMessage(["student.parent"], "parent").Should().BeNull();
    }
}
