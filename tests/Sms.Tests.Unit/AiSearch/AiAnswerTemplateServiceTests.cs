using FluentAssertions;
using Sms.Application.Services.AiSearch;
using Xunit;

namespace Sms.Tests.Unit.AiSearch;

public class AiAnswerTemplateServiceTests
{
    private readonly AiAnswerTemplateService svc = new();

    [Theory]
    [InlineData("en", "Today, 781 students are present out of 842. Absent: 61, attendance: 92.76%.")]
    [InlineData("hi", "आज 842 में से 781 बच्चे उपस्थित हैं। अनुपस्थित: 61, उपस्थिति: 92.76%.")]
    [InlineData("hinglish", "Aaj 842 mein se 781 bachche school aaye hain. Absent: 61, attendance: 92.76%.")]
    public void RenderDailyAttendanceSummary_uses_the_detected_language(string lang, string expected)
    {
        svc.RenderDailyAttendanceSummary(lang, 842, 781, 61, 92.76m).Should().Be(expected);
    }

    [Fact]
    public void RenderWriteBlocked_never_translates_into_performing_the_write()
    {
        svc.RenderWriteBlocked("hinglish").Should().Contain("nahi kar sakta");
        svc.RenderWriteBlocked("en").Should().Contain("cannot modify");
    }

    [Fact]
    public void Falls_back_to_english_for_an_unrecognized_language_code()
    {
        svc.RenderStudentAttendance("fr", "Rahul", 91.2m).Should().Be("Rahul's attendance is 91.2%.");
    }

    [Theory]
    [InlineData(0, "Good morning, Aisha")]
    [InlineData(11, "Good morning, Aisha")]
    [InlineData(12, "Good afternoon, Aisha")]
    [InlineData(16, "Good afternoon, Aisha")]
    [InlineData(17, "Good evening, Aisha")]
    [InlineData(23, "Good evening, Aisha")]
    public void RenderGreeting_buckets_by_hour_of_day_boundaries(int hour, string expected)
    {
        svc.RenderGreeting("en", "Aisha", hour).Should().Be(expected);
    }

    [Theory]
    [InlineData(0, "सुप्रभात, Aisha")]
    [InlineData(12, "नमस्कार, Aisha")]
    [InlineData(17, "शुभ संध्या, Aisha")]
    public void RenderGreeting_uses_the_detected_language(int hour, string expected)
    {
        svc.RenderGreeting("hi", "Aisha", hour).Should().Be(expected);
    }

    [Fact]
    public void RenderGreeting_falls_back_to_english_for_an_unrecognized_language_code()
    {
        svc.RenderGreeting("fr", "Aisha", 9).Should().Be("Good morning, Aisha");
    }

    [Theory]
    [InlineData("en", "Rahul Sharma is a Teacher.")]
    [InlineData("hi", "Rahul Sharma ek Teacher hain.")]
    [InlineData("hinglish", "Rahul Sharma ek Teacher hain.")]
    public void RenderPersonIsTeacher_names_the_role_first_then_subjects(string language, string expectedStart)
    {
        var answer = svc.RenderPersonIsTeacher(language, "Rahul Sharma", ["Mathematics"]);
        answer.Should().StartWith(expectedStart.Split(" hain")[0]); // loose start-of-sentence check; exact strings pinned in Step 3 below
        answer.Should().Contain("Mathematics");
    }

    [Fact]
    public void RenderPersonIsTeacher_lists_multiple_subjects()
    {
        var answer = svc.RenderPersonIsTeacher("en", "Rahul Sharma", ["Mathematics", "Physics"]);
        answer.Should().Contain("Mathematics").And.Contain("Physics");
    }

    [Fact]
    public void RenderPersonIsStudent_includes_the_class_label_when_present()
    {
        var answer = svc.RenderPersonIsStudent("en", "Rahul Verma", "8-A");
        answer.Should().Contain("Rahul Verma").And.Contain("8-A").And.Contain("Student");
    }

    [Fact]
    public void RenderPersonIsStaffLike_uses_the_supplied_role_label_verbatim()
    {
        svc.RenderPersonIsStaffLike("en", "Rahul Khan", "Owner").Should().Contain("Owner");
    }

    [Fact]
    public void RenderNoActiveTrip_and_RenderTripStatus_are_distinct_per_language()
    {
        svc.RenderNoActiveTrip("en").Should().NotBe(svc.RenderNoActiveTrip("hi"));
        svc.RenderTripStatus("en", "BUS-12", "morning", "in_progress")
            .Should().Contain("BUS-12");
    }
}
