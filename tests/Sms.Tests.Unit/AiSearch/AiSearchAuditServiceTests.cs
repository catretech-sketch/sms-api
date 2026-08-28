using System.Data.Common;
using FluentAssertions;
using Sms.Application.Services.AiSearch;
using Sms.Modules.AiSearch.Data;
using Sms.Shared.Kernel.Data;
using Sms.Shared.Kernel.Time;
using Xunit;

namespace Sms.Tests.Unit.AiSearch;

public class AiSearchAuditServiceTests
{
    // No mocking library is referenced by Sms.Tests.Unit; a hand-rolled fake mirrors the
    // pattern already used elsewhere in this suite (e.g. AiClassificationClientTests' FakeHandler).
    private sealed class ThrowingConnectionFactory : IDbConnectionFactory
    {
        public Task<DbConnection> OpenAsync(CancellationToken ct = default) =>
            throw new InvalidOperationException("db down");
    }

    private sealed class FixedClock(DateTime utcNow) : IClock
    {
        public DateTime UtcNow { get; } = utcNow;
    }

    [Fact]
    public async Task LogAsync_swallows_repository_exceptions()
    {
        var repo = new AiSearchLogRepository(new ThrowingConnectionFactory());
        var service = new AiSearchAuditService(repo, new FixedClock(DateTime.UtcNow));

        var act = async () => await service.LogAsync(
            Guid.NewGuid(), Guid.NewGuid(), "school.admin", "q", "en", "DailyAttendanceSummary", 1, true);

        await act.Should().NotThrowAsync();
    }
}
