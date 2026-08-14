using FluentAssertions;
using Sms.Application.Services.Academics;
using Sms.Modules.Academics.Contracts;

namespace Sms.Tests.Unit.Academics;

public class StudentClassScopeTests
{
    private static readonly Guid Tenant = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid Class9A = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Class10B = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static ClassResponse Cls(Guid id, string name, string? grade, string? section, string? subject) =>
        new(id, Tenant, name, grade, section, subject, "101", 0, null);

    private static SubjectResponse Sub(string name, string? teacher = null) =>
        new(Guid.NewGuid(), Tenant, name, name[..Math.Min(2, name.Length)], null, "blue") { TeacherName = teacher };

    [Fact]
    public void PapersForStudent_keeps_this_class_and_unscoped_papers()
    {
        var classIds = new HashSet<Guid> { Class9A };
        var keep = Paper(Class9A, "Mathematics");
        var drop = Paper(Class10B, "Mathematics");
        var schoolWide = Paper(null, "Assembly");

        var result = StudentClassScope.PapersForStudent([keep, drop, schoolWide], classIds);

        result.Select(p => p.Id).Should().BeEquivalentTo(new[] { keep.Id, schoolWide.Id });
    }

    private static ExamPaperResponse Paper(Guid? classId, string subject) =>
        new(Guid.NewGuid(), Tenant, null, classId, subject, subject, null,
            DateTime.UtcNow.Date, "09:00", 45, 50, null, null, null, "Scheduled");

    private static TimetableSlotResponse Slot(string subject, Guid? classId, string? className, string? teacher = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = Tenant,
            Day = "Mon",
            Period = 1,
            Subject = subject,
            ClassId = classId,
            ClassName = className,
            Room = "101",
            StartTime = "08:00",
            EndTime = "08:45",
            TeacherName = teacher,
        };

    [Fact]
    public void SubjectsForStudent_keeps_only_catalog_rows_mapped_to_the_class()
    {
        var math = Sub("Mathematics", "Default Math");
        var sci = Sub("Science", "Default Sci");
        var art = Sub("Art");
        var classes = new[] { Cls(Class9A, "9-A", "9", "A", "Mathematics"), Cls(Class10B, "10-B", "10", "B", "Art") };
        var slots = new[] { Slot("Science", Class9A, "9-A", "Ravi"), Slot("Art", Class10B, "10-B") };

        var result = StudentClassScope.SubjectsForStudent(
            [math, sci, art], classes, slots, grade: "9", section: "A", classLabel: "9-A");

        result.Select(s => s.Name).Should().BeEquivalentTo("Mathematics", "Science");
        result.Should().NotContain(s => s.Name == "Art");
        result.Single(s => s.Name == "Science").TeacherName.Should().Be("Ravi");
        result.Single(s => s.Name == "Mathematics").TeacherName.Should().Be("Default Math");
    }

    [Fact]
    public void SubjectsForStudent_uses_admin_mapped_names_only_when_present()
    {
        var math = Sub("Mathematics");
        var sci = Sub("Science");
        var art = Sub("Art");
        var classes = new[] { Cls(Class9A, "9-A", "9", "A", "Mathematics") };
        var slots = new[] { Slot("Art", Class9A, "9-A") };

        var result = StudentClassScope.SubjectsForStudent(
            [math, sci, art], classes, slots, "9", "A", "9-A", adminMappedNames: ["Science"]);

        result.Select(s => s.Name).Should().Equal("Science");
    }

    [Fact]
    public void SubjectsForStudent_matches_class_by_grade_and_section_without_label()
    {
        var eng = Sub("English");
        var classes = new[] { Cls(Class9A, "Nine A", "9", "A", "English") };

        var result = StudentClassScope.SubjectsForStudent(
            [eng, Sub("History")], classes, [], grade: "9", section: "A", classLabel: null);

        result.Select(s => s.Name).Should().Equal("English");
    }

    [Fact]
    public void SubjectsForStudent_returns_empty_when_class_has_no_mapped_subjects()
    {
        var result = StudentClassScope.SubjectsForStudent(
            [Sub("Mathematics")],
            [Cls(Class9A, "9-A", "9", "A", null)],
            [],
            grade: "9",
            section: "A",
            classLabel: "9-A");

        result.Should().BeEmpty();
    }

    [Fact]
    public void SlotBelongsToStudent_matches_free_text_class_name()
    {
        var classIds = new HashSet<Guid>();
        var slot = Slot("Physics", classId: null, className: "9-A");

        StudentClassScope.SlotBelongsToStudent(slot, classIds, "9", "A", "9-A").Should().BeTrue();
        StudentClassScope.SlotBelongsToStudent(slot, classIds, "10", "B", "10-B").Should().BeFalse();
    }

    [Fact]
    public void ClassMatches_treats_hyphen_and_space_labels_as_the_same_section()
    {
        var ivb = Cls(Class9A, "IV B", "4", "B", "Music");
        StudentClassScope.ClassMatches(ivb, "IV", "B", "IV-B").Should().BeTrue();
        StudentClassScope.ClassMatches(ivb, "IV", "B", "IV B").Should().BeTrue();
    }

    [Fact]
    public void SlotBelongsToStudent_matches_IV_B_class_name_to_IV_B_label()
    {
        var slot = Slot("Music", classId: null, className: "IV B");
        StudentClassScope.SlotBelongsToStudent(slot, new HashSet<Guid>(), "IV", "B", "IV-B")
            .Should().BeTrue();
    }

    [Fact]
    public void SlotBelongsToStudent_drops_leftover_slots_once_the_class_id_is_known()
    {
        var published = Slot("Mathematics", Class9A, "IV-B");
        var leftover = Slot("Old Music", Guid.NewGuid(), "IV-B");
        var ids = new HashSet<Guid> { Class9A };

        StudentClassScope.SlotBelongsToStudent(published, ids, "IV", "B", "IV-B").Should().BeTrue();
        StudentClassScope.SlotBelongsToStudent(leftover, ids, "IV", "B", "IV-B").Should().BeFalse();
    }

    [Fact]
    public void MatchingTimetableClassIds_prefers_the_named_section_over_subject_class_rows()
    {
        var homeroom = Cls(Class9A, "IV-B", "IV", "B", null);
        var music = Cls(Class10B, "IV-B Music", "IV", "B", "Music");

        StudentClassScope.MatchingTimetableClassIds([homeroom, music], "IV", "B", "IV-B")
            .Should().Equal(Class9A);
    }
}
