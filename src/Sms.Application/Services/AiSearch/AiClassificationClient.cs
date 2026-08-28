using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Sms.Shared.Kernel.AiSearch;

namespace Sms.Application.Services.AiSearch;

public sealed class AiClassificationClient(HttpClient http, IOptions<AiSearchOptions> options) : IAiClassificationClient
{
    private const string SystemPrompt = """
        You are the School Management System's read-only AI Search Assistant.
        You only identify which read-only search intent and filters match the user's question.
        You never generate INSERT, UPDATE, DELETE, MERGE, UPSERT, DROP, ALTER, TRUNCATE, CREATE, or EXEC.
        You never determine or override TenantId, UserId, role, or permissions — the backend handles that.
        If the question asks for a modification (e.g. "mark X present", "delete Y"), set intent to
        "WriteRequestDetected". If the question doesn't match any known intent, set intent to "Unsupported".
        Detect the language style as one of: en, hi, hinglish. Support mixed-language questions.
        Known intents: DailyAttendanceSummary, ClassAttendance, SectionAttendance, StudentAttendance,
        TeacherAttendance, StaffAttendance, DashboardSummary, StudentSearch, StudentDetails, TeacherSearch,
        StaffSearch, UpcomingExamSearch, TestSearch, HomeworkSearch, SubjectSearch, BusLocationSearch.
        Always call the classify_query tool with your answer — never respond in plain text.
        """;

    private static readonly object[] Tools =
    [
        new
        {
            name = "classify_query",
            description = "Classify a school-search question into language, intent, and filters.",
            input_schema = new
            {
                type = "object",
                properties = new
                {
                    language = new { type = "string", @enum = new[] { "en", "hi", "hinglish" } },
                    intent = new { type = "string" },
                    filters = new
                    {
                        type = "object",
                        properties = new
                        {
                            studentName = new { type = "string" },
                            className = new { type = "string" },
                            section = new { type = "string" },
                            dateExpression = new { type = "string" },
                            targetSelf = new { type = "boolean" }
                        }
                    }
                },
                required = new[] { "language", "intent", "filters" }
            }
        }
    ];

    public async Task<AiClassificationResult> ClassifyAsync(string query, CancellationToken ct = default)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(options.Value.TimeoutSeconds));

            var request = new HttpRequestMessage(HttpMethod.Post, "/v1/messages")
            {
                Content = JsonContent.Create(new
                {
                    model = options.Value.Model,
                    max_tokens = 512,
                    system = SystemPrompt,
                    tools = Tools,
                    tool_choice = new { type = "tool", name = "classify_query" },
                    messages = new[] { new { role = "user", content = query } }
                })
            };
            request.Headers.Add("x-api-key", options.Value.ApiKey);
            request.Headers.Add("anthropic-version", "2023-06-01");

            var response = await http.SendAsync(request, cts.Token);
            response.EnsureSuccessStatusCode();

            using var stream = await response.Content.ReadAsStreamAsync(cts.Token);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cts.Token);

            var toolUse = doc.RootElement.GetProperty("content")
                .EnumerateArray()
                .First(b => b.GetProperty("type").GetString() == "tool_use");
            var input = toolUse.GetProperty("input");

            var filtersEl = input.GetProperty("filters");
            var filters = new AiSearchFilters(
                filtersEl.TryGetProperty("studentName", out var sn) ? sn.GetString() : null,
                filtersEl.TryGetProperty("className", out var cn) ? cn.GetString() : null,
                filtersEl.TryGetProperty("section", out var se) ? se.GetString() : null,
                filtersEl.TryGetProperty("dateExpression", out var de) ? de.GetString() : null,
                filtersEl.TryGetProperty("targetSelf", out var ts) && ts.GetBoolean());

            return new AiClassificationResult(
                input.GetProperty("language").GetString() ?? "en",
                input.GetProperty("intent").GetString() ?? "Unsupported",
                filters);
        }
        catch (Exception) when (ct.IsCancellationRequested is false)
        {
            return new AiClassificationResult("en", "Unsupported", new AiSearchFilters(null, null, null, null, false));
        }
    }
}
