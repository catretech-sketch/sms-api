using Sms.Modules.Issues;
using Xunit;

namespace Sms.Tests.Unit.Issues;

public class IssueValidationTests
{
    [Theory]
    [InlineData("vehicle", true)]
    [InlineData("student", true)]
    [InlineData("route", true)]
    [InlineData("safety", true)]
    [InlineData("other", true)]
    [InlineData("bogus", false)]
    public void Category_validation_accepts_only_the_five_approved_values(string category, bool expected) =>
        Assert.Equal(expected, IssueEnums.ValidCategories.Contains(category));

    [Theory]
    [InlineData("normal", true)]
    [InlineData("high", true)]
    [InlineData("emergency", true)]
    [InlineData("urgent", false)]
    public void Priority_validation_accepts_only_the_three_approved_values(string priority, bool expected) =>
        Assert.Equal(expected, IssueEnums.ValidPriorities.Contains(priority));

    [Theory]
    [InlineData("open", true)]
    [InlineData("in_progress", true)]
    [InlineData("resolved", true)]
    [InlineData("closed", true)]
    [InlineData("pending", false)]
    public void Status_validation_accepts_only_the_four_approved_values(string status, bool expected) =>
        Assert.Equal(expected, IssueEnums.ValidStatuses.Contains(status));
}
