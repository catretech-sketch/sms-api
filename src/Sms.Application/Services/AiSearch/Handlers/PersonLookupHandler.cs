using Sms.Modules.Staffing.Data;

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
/// </summary>
public sealed class PersonLookupHandler(
    IPersonResolver resolver, IAiAnswerTemplateService templates, TeacherRepository teachers)
    : IAiIntentHandler
{
    public const string IntentName = "PersonLookup";

    public string Intent => IntentName;

    public async Task<AiSearchResponse> HandleAsync(
        AiAuthorizationResult auth, string language, int page, int pageSize, CancellationToken ct = default)
    {
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

        var match = matches[0];
        var answer = await RenderAsync(match, language, ct);
        var data = new { id = match.Id, name = match.Name, type = match.Type, detail = match.Detail };
        return AiSearchResponse.Ok(language, IntentName, answer, data, 1, pageSize, 1, false)
            with { ConversationUpdate = new AiConversationUpdate(match.Id, match.Type, null) };
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
}
