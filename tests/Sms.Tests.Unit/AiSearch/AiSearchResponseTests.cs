using System.Text.Json;
using FluentAssertions;
using Sms.Application.Services.AiSearch;
using Xunit;

namespace Sms.Tests.Unit.AiSearch;

public class AiSearchResponseTests
{
    [Fact]
    public void Ok_sets_status_to_success()
    {
        var response = AiSearchResponse.Ok("en", "StudentSearch", "ok", null, 1, 20, 0, false);
        response.Status.Should().Be("success");
    }

    [Fact]
    public void Terminal_uses_the_caller_supplied_status()
    {
        var response = AiSearchResponse.Terminal("en", "Forbidden", "no", "forbidden");
        response.Status.Should().Be("forbidden");
        response.Intent.Should().Be("Forbidden", "intent keeps its existing meaning unchanged - non-breaking");
    }

    [Fact]
    public void Fail_sets_status_to_error()
    {
        var response = AiSearchResponse.Fail("InvalidRequest", "bad");
        response.Status.Should().Be("error");
    }

    [Fact]
    public void NeedsClarification_carries_candidates_with_a_real_count_and_no_ids_in_the_serialized_data()
    {
        var candidates = new[]
        {
            new PersonCandidate("Rahul Sharma", "teacher", "Mathematics"),
            new PersonCandidate("Rahul Verma", "student", "Class 8A"),
        };
        var response = AiSearchResponse.NeedsClarification(
            "en", "PersonLookup", "I found two people named Rahul. Which one do you mean?", candidates);

        response.Status.Should().Be("needs_clarification");
        response.Intent.Should().Be("PersonLookup");
        response.Count.Should().Be(2);

        var json = JsonSerializer.Serialize(response, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower });
        json.Should().NotContain("Guid").And.NotMatch("*id*:*-*-*-*-*"); // no GUID-shaped id anywhere in the payload
    }

    [Fact]
    public void ConversationUpdate_is_never_serialized()
    {
        var response = AiSearchResponse.Ok("en", "PersonLookup", "x", null, 1, 1, 1, false)
            with { ConversationUpdate = new AiConversationUpdate(Guid.NewGuid(), "teacher", null) };

        var json = JsonSerializer.Serialize(response);
        json.Should().NotContain("ConversationUpdate").And.NotContain("conversation_update");
    }
}
