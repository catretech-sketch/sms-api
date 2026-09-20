using System.Text.Json;
using Sms.Shared.Kernel.Http;

namespace Sms.Tests.Unit.Http;

public sealed class UtcDateTimeJsonConverterTests
{
    private static readonly JsonSerializerOptions Options = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var o = new JsonSerializerOptions();
        o.Converters.Add(new UtcDateTimeJsonConverter());
        o.Converters.Add(new UtcNullableDateTimeJsonConverter());
        return o;
    }

    private sealed record Payload(DateTime SentAt, DateTime? LastAt);

    [Fact]
    public void Write_unspecified_sql_datetime_emits_Z_suffix()
    {
        var payload = new Payload(
            DateTime.SpecifyKind(new DateTime(2026, 9, 18, 4, 30, 0), DateTimeKind.Unspecified),
            DateTime.SpecifyKind(new DateTime(2026, 9, 18, 5, 0, 0), DateTimeKind.Unspecified));

        var json = JsonSerializer.Serialize(payload, Options);

        Assert.Contains("\"SentAt\":\"2026-09-18T04:30:00.000Z\"", json);
        Assert.Contains("\"LastAt\":\"2026-09-18T05:00:00.000Z\"", json);
    }

    [Fact]
    public void Write_null_nullable_emits_null()
    {
        var payload = new Payload(
            DateTime.SpecifyKind(new DateTime(2026, 9, 18, 4, 30, 0), DateTimeKind.Utc),
            null);

        var json = JsonSerializer.Serialize(payload, Options);

        Assert.Contains("\"LastAt\":null", json);
    }
}
