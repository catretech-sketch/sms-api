using Sms.Application.Common;
using Sms.Application.Services.Comms;
using Sms.Modules.Academics.Contracts;
using Sms.Modules.Academics.Data;
using Sms.Modules.Comms;
using Sms.Modules.Sis.Data;
using Sms.Modules.Tenancy.Data;
using Sms.Shared.Kernel.Results;
using Sms.Application.Services.Realtime;
using Sms.Shared.Kernel.Tenancy;

namespace Sms.Application.Services.Academics;

public interface IExamMarksNotifyService
{
    Task<ApiResult<NotifyExamMarksResponse>> NotifyPublishedAsync(Guid examPaperId, CancellationToken ct = default);
}

public sealed record NotifyExamMarksResponse(int ParentReach, int StudentReach, int EmailsSent);

/// Publishes exam marks to parent + student apps and emails guardians (same announcement pipeline as CRM).
public sealed class ExamMarksNotifyService(
    ExamRepository exams,
    ClassRepository classes,
    StudentRepository students,
    ClientRepository clients,
    IAnnouncementService announcements,
    ITenantContext tenant,
    ILiveBroadcaster live) : IExamMarksNotifyService
{
    public async Task<ApiResult<NotifyExamMarksResponse>> NotifyPublishedAsync(
        Guid examPaperId, CancellationToken ct = default)
    {
        if (tenant.TenantId is not { } tid)
            return ApiResult<NotifyExamMarksResponse>.Fail(new Error("forbidden", "no tenant context"), 403);

        var paper = await exams.GetExamPaperAsync(examPaperId, ct);
        if (paper is null)
            return ApiResult<NotifyExamMarksResponse>.Fail(new Error("not_found", "resource not found"), 404);

        var exam = paper.ExamId is { } eid ? await exams.GetExamAsync(eid, ct) : null;
        var examName = exam?.Name ?? "Exam";
        var subject = string.IsNullOrWhiteSpace(paper.Subject) ? "paper" : paper.Subject.Trim();

        string? classLabel = null;
        if (paper.ClassId is { } cid)
        {
            var cls = await classes.GetAsync(cid, ct);
            if (cls is not null)
                classLabel = string.IsNullOrWhiteSpace(cls.Name)
                    ? $"{cls.Grade}-{cls.Section}".Trim('-')
                    : cls.Name.Trim();
        }

        var classSuffix = string.IsNullOrWhiteSpace(classLabel) ? "" : $" · {classLabel}";
        var title = $"Marks published — {examName}{classSuffix} · {subject}";
        var schoolName = (await clients.GetAsync(tid, ct))?.Name ?? "your school";
        var body = $"{schoolName}: Marks for {subject} ({examName}{classSuffix}) are now available. " +
                   "Open the parent or student app to view marks. Full report cards follow when results are published.";

        var rosterEmails = new List<string>();
        if (paper.ClassId is { } classId)
        {
            string? cursor = null;
            do
            {
                var (rows, next) = await students.ListByClassPagedAsync(classId, 200, cursor, ct);
                foreach (var s in rows)
                    AddEmail(rosterEmails, s.Email);
                cursor = next;
            } while (cursor is not null);
        }

        var role = tenant.UserId is not null ? "teacher" : null;
        var creatorId = tenant.UserId;
        var eventDate = paper.Date?.ToString("yyyy-MM-dd");

        int parentReach = 0;
        int studentReach = 0;
        int emailsSent = 0;

        var parentRes = await announcements.CreateAsync(new CreateAnnouncementRequest(
            title, body, "exam_marks", "parent",
            rosterEmails, null, ["email", "app"],
            schoolName, eventDate, "marks"), creatorId, role, ct);
        if (parentRes.IsSuccess && parentRes.Data is not null)
        {
            parentReach = parentRes.Data.Reach;
            emailsSent += parentReach;
        }

        var studentRes = await announcements.CreateAsync(new CreateAnnouncementRequest(
            title, body, "exam_marks", "student",
            rosterEmails, null, ["email", "app"],
            schoolName, eventDate, "marks"), creatorId, role, ct);
        if (studentRes.IsSuccess && studentRes.Data is not null)
            studentReach = studentRes.Data.Reach;

        await live.PublishAsync(tid, LiveEventTypes.Grades, ct: ct);
        return ApiResult<NotifyExamMarksResponse>.Ok(
            new NotifyExamMarksResponse(parentReach, studentReach, emailsSent));
    }

    private static void AddEmail(List<string> list, string? email)
    {
        var v = (email ?? "").Trim();
        if (v.Contains('@') && v.Length > 3) list.Add(v);
    }
}
