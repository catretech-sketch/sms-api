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
    [InlineData("en", "Rahul Sharma is a Teacher. He/She teaches Mathematics.")]
    [InlineData("hi", "Rahul Sharma एक Teacher हैं। ये Mathematics पढ़ाते हैं।")]
    [InlineData("hinglish", "Rahul Sharma ek Teacher hain. Ye Mathematics padhate hain.")]
    public void RenderPersonIsTeacher_pins_the_exact_string_per_language(string language, string expected)
    {
        svc.RenderPersonIsTeacher(language, "Rahul Sharma", ["Mathematics"]).Should().Be(expected);
    }

    [Fact]
    public void RenderPersonIsTeacher_lists_multiple_subjects()
    {
        var answer = svc.RenderPersonIsTeacher("en", "Rahul Sharma", ["Mathematics", "Physics"]);
        answer.Should().Contain("Mathematics").And.Contain("Physics");
    }

    [Fact]
    public void RenderPersonIsTeacher_hi_and_hinglish_are_genuinely_different_strings()
    {
        // Finding 4(a): "hi" must be real Devanagari script, not Romanized Hinglish copied verbatim.
        var hi = svc.RenderPersonIsTeacher("hi", "Rahul Sharma", ["Mathematics"]);
        var hinglish = svc.RenderPersonIsTeacher("hinglish", "Rahul Sharma", ["Mathematics"]);
        hi.Should().NotBe(hinglish);
    }

    [Fact]
    public void RenderPersonIsTeacher_has_no_dead_conditional_between_one_and_many_subjects()
    {
        // Finding 4(b): both arms of the old ternary produced the identical string ("He/She teaches"
        // regardless of count) -- there is no genuine singular/plural distinction in English here, so
        // the sentence shape must be identical for one subject and for many, just with a longer list.
        var one = svc.RenderPersonIsTeacher("en", "Rahul Sharma", ["Mathematics"]);
        var many = svc.RenderPersonIsTeacher("en", "Rahul Sharma", ["Mathematics", "Physics"]);
        one.Should().Be("Rahul Sharma is a Teacher. He/She teaches Mathematics.");
        many.Should().Be("Rahul Sharma is a Teacher. He/She teaches Mathematics, Physics.");
    }

    [Theory]
    [InlineData("en", "Rahul Sharma is a Teacher. No subjects are on file.")]
    [InlineData("hi", "Rahul Sharma एक Teacher हैं। अभी तक कोई विषय दर्ज नहीं है।")]
    [InlineData("hinglish", "Rahul Sharma ek Teacher hain. Abhi tak koi subject darj nahi hai.")]
    public void RenderPersonIsTeacher_with_no_subjects_on_file_has_no_awkward_trailing_punctuation(
        string language, string expected)
    {
        svc.RenderPersonIsTeacher(language, "Rahul Sharma", []).Should().Be(expected);
    }

    [Fact]
    public void RenderPersonIsStudent_includes_the_class_label_when_present()
    {
        var answer = svc.RenderPersonIsStudent("en", "Rahul Verma", "8-A");
        answer.Should().Contain("Rahul Verma").And.Contain("8-A").And.Contain("Student");
    }

    [Theory]
    [InlineData("en", "Rahul Verma is a Student.")]
    [InlineData("hi", "Rahul Verma एक Student हैं।")]
    [InlineData("hinglish", "Rahul Verma ek Student hain.")]
    public void RenderPersonIsStudent_with_no_class_label_has_no_awkward_trailing_punctuation(
        string language, string expected)
    {
        svc.RenderPersonIsStudent(language, "Rahul Verma", null).Should().Be(expected);
        svc.RenderPersonIsStudent(language, "Rahul Verma", "").Should().Be(expected);
    }

    [Fact]
    public void RenderPersonIsStaffLike_uses_the_supplied_role_label_verbatim()
    {
        svc.RenderPersonIsStaffLike("en", "Rahul Khan", "Owner").Should().Contain("Owner");
    }

    [Theory]
    [InlineData("en", "Rahul Khan is a Owner.")]
    [InlineData("hi", "Rahul Khan एक Owner हैं।")]
    [InlineData("hinglish", "Rahul Khan ek Owner hain.")]
    public void RenderPersonIsStaffLike_pins_the_exact_string_per_language(string language, string expected)
    {
        svc.RenderPersonIsStaffLike(language, "Rahul Khan", "Owner").Should().Be(expected);
    }

    [Fact]
    public void RenderPersonIsStaffLike_hi_and_hinglish_are_genuinely_different_strings()
    {
        // Finding 4(a): "hi" must be real Devanagari script, not Romanized Hinglish copied verbatim.
        var hi = svc.RenderPersonIsStaffLike("hi", "Rahul Khan", "Owner");
        var hinglish = svc.RenderPersonIsStaffLike("hinglish", "Rahul Khan", "Owner");
        hi.Should().NotBe(hinglish);
    }

    [Fact]
    public void RenderNoActiveTrip_and_RenderTripStatus_are_distinct_per_language()
    {
        svc.RenderNoActiveTrip("en").Should().NotBe(svc.RenderNoActiveTrip("hi"));
        svc.RenderTripStatus("en", "BUS-12", "morning", "in_progress")
            .Should().Contain("BUS-12");
    }
}
