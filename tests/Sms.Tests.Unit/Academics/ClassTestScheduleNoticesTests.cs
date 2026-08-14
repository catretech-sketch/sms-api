using FluentAssertions;
using Sms.Application.Services.Academics;
using Sms.Modules.Academics.Contracts;

namespace Sms.Tests.Unit.Academics;

public class ClassTestScheduleNoticesTests
{
    private static readonly Guid Tenant = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private static PublishSnapshotResponse Snap(string? draft, string? published = null) =>
        new(Guid.NewGuid(), Tenant, draft, published, DateTime.UtcNow, null);

    [Fact]
    public void NewTests_returns_draft_tests_that_were_not_in_the_previous_snapshot()
    {
        var draft = """[{"id":1,"cls":"IX-A","subject":"Mathematics","title":"Unit Test 2","date":"2026-06-20"}]""";

        var added = ClassTestScheduleNotices.NewTests(previous: null, Snap(draft));

        added.Should().ContainSingle();
        added[0].Title.Should().Be("Unit Test 2");
        added[0].Subject.Should().Be("Mathematics");
        added[0].ClassName.Should().Be("IX-A");
        added[0].Date.Should().Be(new DateTime(2026, 6, 20));
    }

    [Fact]
    public void NewTests_skips_tests_already_saved_so_repeat_draft_saves_do_not_spam()
    {
        var draft = """[{"id":1,"cls":"IX-A","subject":"Mathematics","title":"Unit Test 2","date":"2026-06-20"}]""";
        var previous = Snap(draft);

        ClassTestScheduleNotices.NewTests(previous, Snap(draft)).Should().BeEmpty();
    }

    [Fact]
    public void NewTests_notifies_only_the_newly_added_row()
    {
        var previous = Snap("""[{"id":1,"title":"Unit Test 2","cls":"IX-A","subject":"Mathematics","date":"2026-06-20"}]""");
        var next = Snap("""[{"id":1,"title":"Unit Test 2","cls":"IX-A","subject":"Mathematics","date":"2026-06-20"},{"id":2,"title":"Oral quiz","cls":"IX-B","subject":"English","date":"2026-06-22"}]""");

        var added = ClassTestScheduleNotices.NewTests(previous, next);

        added.Should().ContainSingle();
        added[0].Title.Should().Be("Oral quiz");
        added[0].ClassName.Should().Be("IX-B");
    }

    [Fact]
    public void NewTests_reads_snake_case_class_name_from_published_json()
    {
        var published = """[{"id":"p1","title":"Mid-term","subject":"Science","class_name":"X-A","date":"2026-07-01"}]""";

        var added = ClassTestScheduleNotices.NewTests(null, Snap(draft: null, published));

        added.Should().ContainSingle();
        added[0].Title.Should().Be("Mid-term");
        added[0].ClassName.Should().Be("X-A");
        added[0].Subject.Should().Be("Science");
    }
}
