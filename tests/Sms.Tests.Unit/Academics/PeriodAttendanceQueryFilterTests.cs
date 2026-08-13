using Sms.Modules.Academics.Contracts;
using Sms.Modules.Academics.Data;

namespace Sms.Tests.Unit.Academics;

public class PeriodAttendanceQueryFilterTests
{
    [Fact]
    public void Build_maps_status_subject_and_page_shape()
    {
        var query = new PeriodAttendanceAdvancedQuery(
            new DateOnly(2026, 8, 1),
            new DateOnly(2026, 8, 13),
            null,
            null,
            null,
            "Music",
            null,
            null,
            null,
            null,
            "absent",
            null,
            2,
            10);

        var command = PeriodAttendanceQuerySql.Build(query);

        Assert.Contains("LOWER(LTRIM(RTRIM(par.Subject))) = LOWER(LTRIM(RTRIM(@Subject)))", command.Sql);
        Assert.Contains("par.Status = @Status", command.Sql);
        Assert.Equal("Music", command.Parameters.Get<string?>("Subject"));
        Assert.Equal("absent", command.Parameters.Get<string?>("Status"));
        Assert.Equal(10, command.Parameters.Get<int>("Offset"));
        Assert.Equal(10, command.PageSize);
        Assert.Equal(2, command.Page);
    }

    [Theory]
    [InlineData(0, 0, 1, 25)]
    [InlineData(-4, -8, 1, 25)]
    [InlineData(3, 101, 3, 100)]
    [InlineData(1, 1, 1, 1)]
    public void Build_normalizes_one_based_page_and_page_size(
        int requestedPage,
        int requestedPageSize,
        int expectedPage,
        int expectedPageSize)
    {
        var query = new PeriodAttendanceAdvancedQuery(
            new DateOnly(2026, 8, 1),
            new DateOnly(2026, 8, 13),
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            requestedPage,
            requestedPageSize);

        var command = PeriodAttendanceQuerySql.Build(query);

        Assert.Equal(expectedPage, command.Page);
        Assert.Equal(expectedPageSize, command.PageSize);
        Assert.Equal((expectedPage - 1) * expectedPageSize, command.Parameters.Get<int>("Offset"));
    }

    [Fact]
    public void Repository_exposes_search_async_with_advanced_page_contract()
    {
        var method = typeof(PeriodAttendanceQueryRepository).GetMethod(
            nameof(PeriodAttendanceQueryRepository.SearchAsync),
            [typeof(PeriodAttendanceAdvancedQuery), typeof(CancellationToken)]);

        Assert.NotNull(method);
        Assert.Equal(typeof(Task<PeriodAttendanceAdvancedPage>), method!.ReturnType);
    }
}
