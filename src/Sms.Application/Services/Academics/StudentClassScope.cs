using Sms.Modules.Academics.Contracts;

namespace Sms.Application.Services.Academics;

/// Matches a roster student (grade / section / class label) to classes, timetable slots,
/// and the subject catalog rows actually taught to that class.
public static class StudentClassScope
{
    public static bool ClassMatches(ClassResponse c, string? grade, string? section, string? classLabel)
    {
        if (!string.IsNullOrWhiteSpace(classLabel)
            && (LabelsMatch(c.Name, classLabel)
                || LabelsMatch($"{c.Grade}-{c.Section}", classLabel)))
            return true;
        if (!string.IsNullOrWhiteSpace(grade)
            && !string.IsNullOrWhiteSpace(section)
            && LabelsMatch(c.Grade, grade)
            && LabelsMatch(c.Section, section))
            return true;
        return false;
    }

    public static bool SlotBelongsToStudent(
        TimetableSlotResponse s, IReadOnlySet<Guid> classIds, string? grade, string? section, string? classLabel)
    {
        if (classIds.Count > 0)
            return s.ClassId is Guid id && classIds.Contains(id);
        return SlotNameMatches(s.ClassName, grade, section, classLabel);
    }

    public static HashSet<Guid> MatchingClassIds(
        IEnumerable<ClassResponse> classes, string? grade, string? section, string? classLabel) =>
        classes.Where(c => ClassMatches(c, grade, section, classLabel)).Select(c => c.Id).ToHashSet();

    /// Homeroom / section row only. Subject-class rows that share Grade+Section must not
    /// pull leftover slots from an older timetable publish.
    public static HashSet<Guid> MatchingTimetableClassIds(
        IEnumerable<ClassResponse> classes, string? grade, string? section, string? classLabel)
    {
        var list = classes as IList<ClassResponse> ?? classes.ToList();
        if (!string.IsNullOrWhiteSpace(classLabel))
        {
            var byName = list.Where(c => LabelsMatch(c.Name, classLabel)).Select(c => c.Id).ToHashSet();
            if (byName.Count > 0) return byName;
        }
        return MatchingClassIds(list, grade, section, classLabel);
    }

    public static IReadOnlyList<SubjectResponse> SubjectsForStudent(
        IReadOnlyList<SubjectResponse> catalog,
        IReadOnlyList<ClassResponse> classes,
        IReadOnlyList<TimetableSlotResponse> slots,
        string? grade,
        string? section,
        string? classLabel,
        IReadOnlyList<string>? adminMappedNames = null)
    {
        var classRows = classes.Where(c => ClassMatches(c, grade, section, classLabel)).ToList();
        var classIds = classRows.Select(c => c.Id).ToHashSet();

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (adminMappedNames is { Count: > 0 })
        {
            foreach (var n in adminMappedNames)
                AddName(names, n);
        }
        else
        {
            foreach (var c in classRows)
                AddName(names, c.Subject);
            foreach (var s in slots)
            {
                if (!SlotBelongsToStudent(s, classIds, grade, section, classLabel)) continue;
                AddName(names, s.Subject);
            }
        }

        var teacherBySubject = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in slots)
        {
            if (string.IsNullOrWhiteSpace(s.TeacherName) || string.IsNullOrWhiteSpace(s.Subject)) continue;
            if (!SlotBelongsToStudent(s, classIds, grade, section, classLabel)) continue;
            teacherBySubject[s.Subject.Trim()] = s.TeacherName;
        }

        return catalog
            .Where(s => names.Contains(s.Name))
            .Select(s => teacherBySubject.TryGetValue(s.Name, out var tn) ? s with { TeacherName = tn } : s)
            .ToList();
    }

    public static IReadOnlyList<ExamPaperResponse> PapersForStudent(
        IReadOnlyList<ExamPaperResponse> papers, IReadOnlySet<Guid> classIds)
    {
        if (classIds.Count == 0) return papers;
        return papers.Where(p => p.ClassId is null || classIds.Contains(p.ClassId.Value)).ToList();
    }

    private static void AddName(HashSet<string> names, string? raw)
    {
        var n = (raw ?? "").Trim();
        if (n.Length > 0) names.Add(n);
    }

    private static bool SlotNameMatches(string? className, string? grade, string? section, string? classLabel)
    {
        var name = (className ?? "").Trim();
        if (name.Length == 0) return false;
        if (!string.IsNullOrWhiteSpace(classLabel) && LabelsMatch(name, classLabel))
            return true;
        if (!string.IsNullOrWhiteSpace(grade) && !string.IsNullOrWhiteSpace(section)
            && LabelsMatch(name, $"{grade.Trim()}-{section.Trim()}"))
            return true;
        return false;
    }

    /// Normalizes away whitespace/hyphen/underscore differences before comparing two class-shaped
    /// labels (e.g. a stored <c>"8-A"</c> ClassLabel against a free-text filter like <c>"8A"</c>).
    /// Public so callers outside this file's own scope-matching helpers (e.g. AI search's
    /// class-attendance resolution and per-teacher section validation) can reuse the exact same
    /// normalization instead of re-implementing it.
    public static bool LabelsMatch(string? a, string? b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return false;
        if (string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase)) return true;
        return string.Equals(CompactLabel(a), CompactLabel(b), StringComparison.OrdinalIgnoreCase);
    }

    public static string CompactLabel(string raw) =>
        string.Concat(raw.Where(c => !char.IsWhiteSpace(c) && c is not '-' and not '_'));
}
