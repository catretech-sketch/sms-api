using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
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

    private static string? RecordedSystemPrompt;

    private sealed class CapturingHandler(string jsonToolInput, Action<string?> captureSystemPrompt) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct)
        {
            var requestBody = await request.Content!.ReadAsStringAsync(ct);
            using var requestDoc = JsonDocument.Parse(requestBody);
            captureSystemPrompt(
                requestDoc.RootElement.TryGetProperty("system", out var sys) ? sys.GetString() : null);

            var body = $$"""
            {"content":[{"type":"tool_use","name":"classify_query","input":{{jsonToolInput}}}]}
            """;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
        }
    }

    private static AiClassificationClient MakeClientCapturingSystemPrompt(string toolInputJson)
    {
        var handler = new CapturingHandler(toolInputJson, prompt => RecordedSystemPrompt = prompt);
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
        var httpClient = new HttpClient(new BrokenHandler()) { BaseAddress = new Uri("https://api.anthropic.com") };
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

    [Fact]
    public async Task ClassifyAsync_still_calls_the_configured_BaseUrl_when_HttpClient_BaseAddress_was_never_set()
    {
        // Regression test: previously the "claude" HttpClient never had BaseAddress set (see
        // ServiceCollectionExtensions), so HttpClient.SendAsync threw InvalidOperationException
        // on every call, and that exception was silently swallowed by the catch-all fallback,
        // making every real classification request fail invisibly. Simulate that scenario here
        // (an HttpClient with no BaseAddress) and assert the request still reaches the handler
        // instead of being silently degraded to "Unsupported".
        var handler = new FakeHandler(
            """{"language":"en","intent":"StudentSearch","filters":{"targetSelf":false}}""");
        var httpClient = new HttpClient(handler); // BaseAddress intentionally left unset.
        var options = Options.Create(new AiSearchOptions { ApiKey = "test-key", BaseUrl = "https://api.anthropic.com" });
        var client = new AiClassificationClient(httpClient, options);

        var result = await client.ClassifyAsync("find student Ravi");

        result.Intent.Should().Be("StudentSearch");
        httpClient.BaseAddress.Should().Be(new Uri("https://api.anthropic.com"));
    }

    [Fact]
    public void DI_registration_sets_the_claude_HttpClient_BaseAddress_from_AiSearchOptions_BaseUrl()
    {
        // Reproduces the real ServiceCollectionExtensions.ConfigureSmsServices wiring for the
        // "claude" named HttpClient and proves BaseAddress ends up set from AiSearchOptions.BaseUrl
        // (the fix for the Critical finding), rather than being left null.
        var services = new ServiceCollection();
        services.Configure<AiSearchOptions>(o =>
        {
            o.BaseUrl = "https://api.anthropic.com";
            o.ApiKey = "test-key";
        });
        services.AddHttpClient("claude", (sp, client) =>
        {
            var aiOptions = sp.GetRequiredService<IOptions<AiSearchOptions>>().Value;
            client.BaseAddress = new Uri(aiOptions.BaseUrl);
        });

        using var provider = services.BuildServiceProvider();
        var httpClient = provider.GetRequiredService<IHttpClientFactory>().CreateClient("claude");

        httpClient.BaseAddress.Should().Be(new Uri("https://api.anthropic.com"));
    }

    [Fact]
    public async Task ClassifyAsync_parses_languageDirective_when_present()
    {
        var client = MakeClient("""
            {"language":"hinglish","intent":"DailyAttendanceSummary",
             "filters":{"studentName":null,"className":null,"section":null,"dateExpression":"aaj","targetSelf":false},
             "languageDirective":"hi"}
            """);

        var result = await client.ClassifyAsync("Hindi mein batao, aaj kitne bachche aaye?");

        result.LanguageDirective.Should().Be("hi");
    }

    [Fact]
    public async Task ClassifyAsync_defaults_languageDirective_to_null_when_absent()
    {
        var client = MakeClient("""
            {"language":"en","intent":"StudentSearch",
             "filters":{"studentName":"Rahul","className":null,"section":null,"dateExpression":null,"targetSelf":false}}
            """);

        var result = await client.ClassifyAsync("who is Rahul");

        result.LanguageDirective.Should().BeNull();
    }

    [Fact]
    public async Task ClassifyAsync_with_a_hint_sends_the_prior_entity_in_the_system_prompt()
    {
        RecordedSystemPrompt = null;
        var client = MakeClientCapturingSystemPrompt("""
            {"language":"en","intent":"PersonLookup",
             "filters":{"studentName":null,"className":null,"section":null,"dateExpression":null,"targetSelf":false}}
            """);

        await client.ClassifyAsync("Kya padhate hain?", new AiConversationHint("Rahul Sharma", "teacher"));

        RecordedSystemPrompt.Should().Contain("Rahul Sharma").And.Contain("teacher");
    }
}
