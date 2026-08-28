using FluentAssertions;
using Sms.Application.Services.AiSearch;
using Xunit;

namespace Sms.Tests.Unit.AiSearch;

public class DateExpressionResolverTests
{
    private static readonly DateOnly Today = new(2026, 8, 28); // Friday

    [Theory]
    [InlineData("today")]
    [InlineData("aaj")]
    [InlineData("आज")]
    [InlineData(null)]
    public void Today_variants_resolve_to_today(string? expr)
    {
        var (from, to) = DateExpressionResolver.Resolve(expr, Today);
        from.Should().Be(Today);
        to.Should().Be(Today);
    }

    [Theory]
    [InlineData("yesterday")]
    [InlineData("kal")]
    [InlineData("कल")]
    public void Yesterday_variants_resolve_to_one_day_back(string expr)
    {
        var (from, to) = DateExpressionResolver.Resolve(expr, Today);
        from.Should().Be(Today.AddDays(-1));
        to.Should().Be(Today.AddDays(-1));
    }

    [Fact]
    public void This_week_resolves_from_monday_to_today()
    {
        var (from, to) = DateExpressionResolver.Resolve("this week", Today);
        from.Should().Be(new DateOnly(2026, 8, 24)); // Monday of that week
        to.Should().Be(Today);
    }

    [Fact]
    public void Last_week_resolves_to_the_prior_monday_through_sunday()
    {
        var (from, to) = DateExpressionResolver.Resolve("pichle week", Today);
        from.Should().Be(new DateOnly(2026, 8, 17));
        to.Should().Be(new DateOnly(2026, 8, 23));
    }

    [Fact]
    public void This_month_resolves_from_the_first_to_today()
    {
        var (from, to) = DateExpressionResolver.Resolve("is month", Today);
        from.Should().Be(new DateOnly(2026, 8, 1));
        to.Should().Be(Today);
    }

    [Fact]
    public void Unknown_expression_defaults_to_today()
    {
        var (from, to) = DateExpressionResolver.Resolve("next eclipse", Today);
        from.Should().Be(Today);
        to.Should().Be(Today);
    }
}
