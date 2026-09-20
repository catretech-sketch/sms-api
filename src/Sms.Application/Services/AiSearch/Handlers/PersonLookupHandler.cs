using Sms.Application.Services.Sis;
using Sms.Modules.Staffing.Data;
using Sms.Shared.Kernel.Auth;

namespace Sms.Application.Services.AiSearch.Handlers;

/// <summary>
/// First consumer of <see cref="IPersonResolver"/> -- answers "Who is X?" style lookups by fanning
/// out across the four person-data sources (students, teachers, staff, admin/owner/principal via the
/// user directory), all already scoped by the caller's authorized <see cref="AiAuthorizationResult"/>.
/// A single unambiguous match renders a person-type-shaped answer and asks the orchestrator to persist
/// it as the conversation's resolved entity (for later follow-ups); multiple matches render a
/// clarification prompt whose Data payload deliberately carries only name/type/detail -- never the
/// real ids -- while the real ids (needed to resolve a follow-up reply) travel out-of-band on
/// <see cref="AiSearchResponse.ConversationUpdate"/>, which is never serialized to the client.
/// <para>
/// Task 12: a conversation follow-up that has already been re-authorized by <c>AiSearchService</c>
/// arrives with <see cref="AiAuthorizationResult.PreResolvedEntityId"/>/<c>PreResolvedEntityType</c>
/// set -- this handler MUST NOT re-derive authorization itself, it only re-fetches that entity's
/// CURRENT name/detail (never trusting a name carried in from prior context) and renders it exactly
/// like a fresh single match would.
/// </para>
/// </summary>
public sealed class PersonLookupHandler(
    IPersonResolver resolver, IAiAnswerTemplateService templates,
    TeacherRepository teachers, StaffRepository staff, ISisService sis, IUserDirectoryLookup users)
    : IAiIntentHandler
{
    public const string IntentName = "PersonLookup";

    public string Intent => IntentName;

    public async Task<AiSearchResponse> HandleAsync(
        AiAuthorizationResult auth, string language, int page, int pageSize, CancellationToken ct = default)
    {
        if (auth.PreResolvedEntityId is { } preResolvedId && auth.PreResolvedEntityType is { } preResolvedType)
        {
            var match = await ResolvePreResolvedAsync(preResolvedId, preResolvedType, ct);
            if (match is null)
                return AiSearchResponse.Terminal(language, "Unsupported", templates.RenderNoMatch(language), "no_match");

            var preAnswer = await RenderAsync(match, language, ct);
            var preData = new { id = match.Id, name = match.Name, type = match.Type, detail = match.Detail };
            return AiSearchResponse.Ok(language, IntentName, preAnswer, preData, 1, pageSize, 1, false)
                with { ConversationUpdate = new AiConversationUpdate(match.Id, match.Type, null) };
        }

        var name = auth.ClampedFilters.StudentName?.Trim();
        if (string.IsNullOrWhiteSpace(name))
            return AiSearchResponse.Terminal(language, "Unsupported", templates.RenderNoMatch(language), "no_match");

        var matches = await resolver.ResolveAsync(name, auth, ct);

        if (matches.Count == 0)
            return AiSearchResponse.Terminal(language, "Unsupported", templates.RenderNoMatch(language), "no_match");

        if (matches.Count > 1)
        {
            var candidates = matches.Select(m => new PersonCandidate(m.Name, m.Type, m.Detail)).ToList();
            var pending = matches.Select(m => new PendingCandidate(m.Id, m.Type)).ToList();
            var clarifyAnswer = templates.RenderNeedsClarification(language, candidates.Count);
            return AiSearchResponse.NeedsClarification(language, IntentName, clarifyAnswer, candidates)
                with { ConversationUpdate = new AiConversationUpdate(null, null, pending) };
        }

        var match2 = matches[0];
        var answer = await RenderAsync(match2, language, ct);
        var data = new { id = match2.Id, name = match2.Name, type = match2.Type, detail = match2.Detail };
        return AiSearchResponse.Ok(language, IntentName, answer, data, 1, pageSize, 1, false)
            with { ConversationUpdate = new AiConversationUpdate(match2.Id, match2.Type, null) };
    }

    private async Task<string> RenderAsync(PersonMatch match, string language, CancellationToken ct)
    {
        if (match.Type == "teacher")
        {
            var rows = await teachers.ListAsync(match.Name, null, null, ct);
            var subjects = rows.FirstOrDefault(t => t.Id == match.Id)?.Subjects ?? [];
            return templates.RenderPersonIsTeacher(language, match.Name, subjects);
        }
        if (match.Type == "student")
            return templates.RenderPersonIsStudent(language, match.Name, match.Detail);

        // staff / admin / owner / principal all share the same staff-like shape -- Detail already
        // carries the exact role label to show (see PersonResolver.ResolveAdminDetails/RoleLabel).
        return templates.RenderPersonIsStaffLike(language, match.Name, match.Detail ?? match.Type);
    }

    /// Re-fetches the real, CURRENT name/detail for a conversation-context id+type -- never trusts
    /// anything about the person beyond the id+type carried in from prior context, since a name could
    /// be stale (e.g. renamed between turns) even when the entity itself is still validly in scope
    /// (that in-scope check was already independently performed by AiSearchService before this handler
    /// was ever reached).
    private async Task<PersonMatch?> ResolvePreResolvedAsync(Guid id, string type, CancellationToken ct)
    {
        switch (type)
        {
            case "student":
                var student = await sis.GetStudentAsync(id, ct);
                return student.IsSuccess
                    ? new PersonMatch(id, student.Data!.Name, "student", student.Data!.ClassLabel)
                    : null;

            case "teacher":
                var teacherRows = await teachers.ListAsync(null, null, null, ct);
                var teacher = teacherRows.FirstOrDefault(t => t.Id == id);
                return teacher is null ? null : new PersonMatch(id, teacher.Name, "teacher", teacher.Department);

            case "staff":
                var staffRows = await staff.ListAsync(null, null, ct);
                var staffMember = staffRows.FirstOrDefault(s => s.Id == id);
                return staffMember is null ? null : new PersonMatch(id, staffMember.Name, "staff", staffMember.Department);

            default: // admin / owner / principal
                var user = await users.GetByIdAsync(id, ct);
                return user is null ? null : new PersonMatch(id, user.Name, user.Type, RoleLabelFor(user.Type));
        }
    }

    private static string RoleLabelFor(string type) => type switch
    {
        "owner" => "Owner",
        "principal" => "Principal",
        _ => "Admin",
    };
}
