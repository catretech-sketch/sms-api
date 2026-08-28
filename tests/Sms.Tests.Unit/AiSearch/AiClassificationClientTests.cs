using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Sms.Application.Services.AiSearch;
using Sms.Shared.Kernel.AiSearch;
using Xunit;

namespace Sms.Tests.Unit.AiSearch;

public class AiClassificationClientTests
{
    private sealed class FakeHandler(string jsonToolInput) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct)
        {
            var body = $$"""
            {"content":[{"type":"tool_use","name":"classify_query","input":{{jsonToolInput}}}]}
            """;
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
            return Task.FromResult(response);
        }
    }

    private static AiClassificationClient MakeClient(string toolInputJson)
    {
        var handler = new FakeHandler(toolInputJson);
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.anthropic.com") };
        var options = Options.Create(new AiSearchOptions { ApiKey = "test-key" });
        return new AiClassificationClient(httpClient, options);
    }

    [Fact]
    public async Task ClassifyAsync_parses_the_tool_use_response_into_a_result()
    {
        var client = MakeClient("""
            {"language":"hinglish","intent":"DailyAttendanceSummary",
             "filters":{"studentName":null,"className":null,"section":null,"dateExpression":"aaj","targetSelf":false}}
            """);

        var result = await client.ClassifyAsync("Aaj kitne bachche school aaye?");

        result.Language.Should().Be("hinglish");
        result.Intent.Should().Be("DailyAttendanceSummary");
        result.Filters.DateExpression.Should().Be("aaj");
        result.Filters.TargetSelf.Should().BeFalse();
    }

    [Fact]
    public async Task ClassifyAsync_returns_Unsupported_on_malformed_response()
    {
        // Genuinely malformed at the top level (not just inside "input").
        var httpClient = new HttpClient(new BrokenHandler());
        var options = Options.Create(new AiSearchOptions { ApiKey = "test-key" });
        var client = new AiClassificationClient(httpClient, options);

        var result = await client.ClassifyAsync("gibberish query");

        result.Intent.Should().Be("Unsupported");
    }

    private sealed class BrokenHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("not json at all {{{", Encoding.UTF8, "application/json")
            });
    }
}
