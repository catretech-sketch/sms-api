using System.Text.Json;
using FluentAssertions;
using Sms.Shared.Kernel.Http;
using Xunit;

namespace Sms.Tests.Unit.Http;

public class SnakeCaseNamingPolicyTests
{
    private static JsonSerializerOptions Opts() =>
        new() { PropertyNamingPolicy = new SnakeCaseNamingPolicy() };

    private sealed record Sample(string AdmissionNo, int AvatarHue);

    [Fact]
    public void Serializes_pascal_properties_as_snake_case()
    {
        var json = JsonSerializer.Serialize(new Sample("ADM-1", 210), Opts());
        json.Should().Contain("\"admission_no\":\"ADM-1\"");
        json.Should().Contain("\"avatar_hue\":210");
    }

    [Theory]
    [InlineData("ID", "id")]
    [InlineData("ClassLabel", "class_label")]
    [InlineData("HTTPStatus", "http_status")]
    public void Converts_names(string input, string expected) =>
        new SnakeCaseNamingPolicy().ConvertName(input).Should().Be(expected);
}
