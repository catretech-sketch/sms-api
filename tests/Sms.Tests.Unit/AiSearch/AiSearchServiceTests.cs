using FluentAssertions;
using Microsoft.Extensions.Options;
using Sms.Application.Services.AiSearch;
using Sms.Shared.Kernel.AiSearch;
using Sms.Shared.Kernel.Authz;
using Sms.Shared.Kernel.Tenancy;
using Xunit;

namespace Sms.Tests.Unit.AiSearch;

/// <summary>
/// Pipeline-order tests for the orchestrator. The point of most of these is not the returned payload
/// but what did NOT happen: no LLM call for a locked tenant, no handler invocation for a write request
/// or an unauthorized caller, no unclamped filters reaching a handler.
/// </summary>
public class AiSearchServiceTests
{
    private static readonly AiSearchFilters EmptyFilters = new(null, null, null, null, false);

    // No mocking library is referenced by Sms.Tests.Unit; hand-rolled fakes mirror the pattern
    // already used elsewhere in this suite (e.g. AiSearchAuditServiceTests).
    private sealed class FakeClassifier(AiClassificationResult result) : IAiClassificationClient
    {
        public int Calls { get; private set; }
        public string? LastQuery { get; private set; }

        public Task<AiClassificationResult> ClassifyAsync(string query, AiConversationHint? hint = null, CancellationToken ct = default)
        {
            Calls++;
            LastQuery = query;
            return Task.FromResult(result);
        }
    }

    private sealed class FakeAuthz(AiAuthorizationResult result) : IAiSearchAuthorizationService
    {
        public int Calls { get; private set; }
        public AiSearchFilters? LastFilters { get; private set; }

        public Task<AiAuthorizationResult> AuthorizeAsync(
            string intent, AiSearchFilters filters, IReadOnlyList<string> callerRoles, CancellationToken ct = default)
        {
            Calls++;
            LastFilters = filters;
            return Task.FromResult(result);
        }
    }

    private sealed class FakeHandler(string intent, AiSearchResponse response) : IAiIntentHandler
    {
        public string Intent { get; } = intent;
        public bool Called { get; private set; }
        public AiAuthorizationResult? LastAuth { get; private set; }
        public int LastPage { get; private set; }
        public int LastPageSize { get; private set; }

        public Task<AiSearchResponse> HandleAsync(
            AiAuthorizationResult auth, string language, int page, int pageSize, CancellationToken ct = default)
        {
            Called = true;
            LastAuth = auth;
            LastPage = page;
            LastPageSize = pageSize;
            return Task.FromResult(response);
        }
    }

    private sealed class ThrowingHandler(string intent, Exception failure) : IAiIntentHandler
    {
        public string Intent { get; } = intent;
        public bool Called { get; private set; }

        public Task<AiSearchResponse> HandleAsync(
            AiAuthorizationResult auth, string language, int page, int pageSize, CancellationToken ct = default)
        {
            Called = true;
            throw failure;
        }
    }

    private sealed record AuditEntry(
        Guid TenantId, Guid UserId, string Role, string Question,
        string? Language, string? Intent, int ResultCount, bool Success);

    private sealed class RecordingAudit : IAiSearchAuditService
    {
        public List<AuditEntry> Entries { get; } = [];

        public Task LogAsync(
            Guid tenantId, Guid userId, string role, string question,
            string? language, string? intent, int resultCount, bool success, CancellationToken ct = default)
        {
            Entries.Add(new AuditEntry(tenantId, userId, role, question, language, intent, resultCount, success));
            return Task.CompletedTask;
        }
    }

    private sealed class FeatureSet(bool has) : ITenantFeatureSet
    {
        public List<string> Asked { get; } = [];

        public bool Has(string feature)
        {
            Asked.Add(feature);
            return has;
        }
    }

    /// A no-op conversation store: LoadAsync always returns null (no stored context) and
    /// SaveAsync/ClearAsync are inert. This is exactly the behaviour every existing (pre-Task-12) test
    /// in this file depends on -- none of them pass a conversation_id, so the orchestrator's
    /// conversation-wiring must be a complete no-op for them.
    private sealed class NullContextStore : IAiConversationContextStore
    {
        public Task<AiConversationContext?> LoadAsync(Guid conversationId, Guid tenantId, Guid userId, CancellationToken ct = default) =>
            Task.FromResult<AiConversationContext?>(null);

        public Task<Guid> SaveAsync(Guid? conversationId, Guid tenantId, Guid userId, AiConversationContext context, CancellationToken ct = default) =>
            Task.FromResult(conversationId ?? Guid.NewGuid());

        public Task ClearAsync(Guid conversationId, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class NullPersonResolver : IPersonResolver
    {
        public Task<IReadOnlyList<PersonMatch>> ResolveAsync(string name, AiAuthorizationResult auth, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<PersonMatch>>([]);

        public Task<bool> IsStillInTeacherScopeAsync(Guid studentId, IReadOnlyList<string> allowedClassNames, CancellationToken ct = default) =>
            Task.FromResult(false);
    }

    /// A scripted conversation-context store that returns a fixed value from LoadAsync (regardless of
    /// the ids passed in -- the orchestrator's own tenant/user scoping is exercised by the integration
    /// tests, not here) and records exactly what SaveAsync was called with, so a test can assert on the
    /// values actually handed to persistence rather than trusting the orchestrator's internal state.
    private sealed class ScriptedContextStore(AiConversationContext? toLoad) : IAiConversationContextStore
    {
        public bool SaveCalled { get; private set; }
        public AiConversationContext? SavedContext { get; private set; }

        public Task<AiConversationContext?> LoadAsync(Guid conversationId, Guid tenantId, Guid userId, CancellationToken ct = default) =>
            Task.FromResult(toLoad);

        public Task<Guid> SaveAsync(Guid? conversationId, Guid tenantId, Guid userId, AiConversationContext context, CancellationToken ct = default)
        {
            SaveCalled = true;
            SavedContext = context;
            return Task.FromResult(conversationId ?? Guid.NewGuid());
        }

        public Task ClearAsync(Guid conversationId, CancellationToken ct = default) => Task.CompletedTask;
    }

    /// Unlike FakeHandler (a fixed canned response), this handler echoes back the `language` argument
    /// it actually receives, so a test can assert on the orchestrator's computed effective language
    /// rather than a value baked into the test's own setup.
    private sealed class EchoLanguageHandler(string intent) : IAiIntentHandler
    {
        public string Intent { get; } = intent;

        public Task<AiSearchResponse> HandleAsync(
            AiAuthorizationResult auth, string language, int page, int pageSize, CancellationToken ct = default) =>
            Task.FromResult(AiSearchResponse.Ok(language, Intent, "ok", null, 1, pageSize, 1, false));
    }

    private sealed class TestTenant(bool isPlatform = false) : ITenantContext
    {
        public Guid? TenantId { get; private set; } = Guid.NewGuid();
        public Guid? UserId { get; private set; } = Guid.NewGuid();
        public bool IsPlatform { get; private set; } = isPlatform;

        public void Set(Guid? tenantId, Guid? userId, bool isPlatform)
        {
            TenantId = tenantId;
            UserId = userId;
            IsPlatform = isPlatform;
        }
    }

    private static AiAuthorizationResult Allowed(string intent, AiSearchFilters? clamped = null) =>
        new(true, intent, null, null, null, clamped ?? EmptyFilters, Unrestricted: true, NameUnmatched: false);

    private static AiAuthorizationResult Denied(AiSearchFilters? clamped = null) =>
        new(false, "Forbidden", null, null, null, clamped ?? EmptyFilters, Unrestricted: false, NameUnmatched: false);

    private static AiSearchService Build(
        IAiClassificationClient classifier,
        IAiSearchAuthorizationService authz,
        IEnumerable<IAiIntentHandler> handlers,
        IAiSearchAuditService audit,
        ITenantFeatureSet features,
        ITenantContext? tenant = null,
        AiSearchOptions? options = null,
        IAiConversationContextStore? contextStore = null,
        IPersonResolver? personResolver = null) =>
        new(classifier, authz, handlers, new AiAnswerTemplateService(), audit,
            contextStore ?? new NullContextStore(), personResolver ?? new NullPersonResolver(),
            tenant ?? new TestTenant(), features, Options.Create(options ?? new AiSearchOptions()));

    [Fact]
    public async Task Feature_gate_blocks_before_any_classification_call_is_made()
    {
        var classifier = new FakeClassifier(new AiClassificationResult("en", "DailyAttendanceSummary", EmptyFilters));
        var audit = new RecordingAudit();
        var features = new FeatureSet(false);
        var service = Build(classifier, new FakeAuthz(Allowed("DailyAttendanceSummary")), [], audit, features);

        var result = await service.SearchAsync(new AiSearchRequest("Aaj kitne bachche aaye?", null, null, null), ["school.admin"]);

        result.Success.Should().BeFalse();
        result.Error!.Code.Should().Be("FeatureNotEnabled");
        classifier.Calls.Should().Be(0, "a locked tenant must never cost an LLM call");
        audit.Entries.Should().BeEmpty();
        features.Asked.Should().Contain(FeatureCatalog.AiSearch);
    }

    [Fact]
    public async Task Platform_context_bypasses_the_tenant_feature_set()
    {
        var classifier = new FakeClassifier(new AiClassificationResult("en", "DailyAttendanceSummary", EmptyFilters));
        var handler = new FakeHandler("DailyAttendanceSummary",
            AiSearchResponse.Ok("en", "DailyAttendanceSummary", "ok", null, 1, 20, 3, false));
        var service = Build(classifier, new FakeAuthz(Allowed("DailyAttendanceSummary")), [handler],
            new RecordingAudit(), new FeatureSet(false), new TestTenant(isPlatform: true));

        var result = await service.SearchAsync(new AiSearchRequest("How many students today?", null, null, null), ["school.admin"]);

        result.Success.Should().BeTrue();
        handler.Called.Should().BeTrue();
    }

    [Fact]
    public async Task Write_request_never_reaches_a_handler_and_returns_WriteBlocked()
    {
        var classifier = new FakeClassifier(new AiClassificationResult("hinglish", "WriteRequestDetected", EmptyFilters));
        var authz = new FakeAuthz(Allowed("WriteRequestDetected"));
        var handler = new FakeHandler("StudentAttendance",
            AiSearchResponse.Ok("hinglish", "StudentAttendance", "x", null, 1, 20, 1, false));
        var audit = new RecordingAudit();
        var service = Build(classifier, authz, [handler], audit, new FeatureSet(true));

        var result = await service.SearchAsync(
            new AiSearchRequest("Rahul ki attendance present kar do", null, null, null), ["school.admin"]);

        result.Intent.Should().Be("WriteBlocked");
        result.Answer.Should().Contain("nahi kar sakta");
        result.Data.Should().BeNull();
        handler.Called.Should().BeFalse();
        authz.Calls.Should().Be(0, "a mutation is refused outright, not evaluated for permission");
        // Finding 5: the audited intent is the classifier's actual attempted intent, not the outcome
        // label — the response's own "intent" field (asserted above) still reports "WriteBlocked".
        audit.Entries.Should().ContainSingle().Which.Intent.Should().Be("WriteRequestDetected");
    }

    [Fact]
    public async Task Unauthorized_intent_returns_Forbidden_without_invoking_any_handler()
    {
        var classifier = new FakeClassifier(new AiClassificationResult("en", "DashboardSummary", EmptyFilters));
        var handler = new FakeHandler("DashboardSummary",
            AiSearchResponse.Ok("en", "DashboardSummary", "x", null, 1, 20, 0, false));
        var audit = new RecordingAudit();
        var service = Build(classifier, new FakeAuthz(Denied()), [handler], audit, new FeatureSet(true));

        var result = await service.SearchAsync(new AiSearchRequest("Aaj ka school summary batao", null, null, null), ["staff"]);

        result.Intent.Should().Be("Forbidden");
        result.Answer.Should().Contain("permission");
        handler.Called.Should().BeFalse();
        // Finding 5: the audited intent is the classifier's actual attempted intent ("DashboardSummary"),
        // not the outcome label — an admin reviewing "WHERE Success = 0" needs to see what was really
        // being asked for. The response's own "intent" field (asserted above) still says "Forbidden".
        audit.Entries.Should().ContainSingle().Which.Intent.Should().Be("DashboardSummary");
    }

    [Fact]
    public async Task Unknown_intent_is_reported_as_Unsupported_rather_than_Forbidden()
    {
        var classifier = new FakeClassifier(new AiClassificationResult("en", "Unsupported", EmptyFilters));
        var authz = new FakeAuthz(Denied());
        var audit = new RecordingAudit();
        var service = Build(classifier, authz, [], audit, new FeatureSet(true));

        var result = await service.SearchAsync(new AiSearchRequest("What's the weather?", null, null, null), ["school.admin"]);

        result.Intent.Should().Be("Unsupported");
        result.Answer.Should().Contain("couldn't understand");
        authz.Calls.Should().Be(0, "an intent nobody implements is not a permission question");
        audit.Entries.Should().ContainSingle().Which.Intent.Should().Be("Unsupported");
    }

    [Fact]
    public async Task Supported_intent_dispatches_to_the_matching_handler_and_returns_its_response()
    {
        var classifier = new FakeClassifier(new AiClassificationResult("en", "ClassAttendance", EmptyFilters));
        var wrong = new FakeHandler("StudentAttendance",
            AiSearchResponse.Ok("en", "StudentAttendance", "wrong", null, 1, 20, 0, false));
        var right = new FakeHandler("ClassAttendance",
            AiSearchResponse.Ok("en", "ClassAttendance", "right", new[] { 1, 2 }, 1, 20, 2, false));
        var service = Build(classifier, new FakeAuthz(Allowed("ClassAttendance")), [wrong, right],
            new RecordingAudit(), new FeatureSet(true));

        var result = await service.SearchAsync(new AiSearchRequest("Class 8A attendance", null, null, null), ["school.admin"]);

        result.Success.Should().BeTrue();
        result.Answer.Should().Be("right");
        right.Called.Should().BeTrue();
        wrong.Called.Should().BeFalse();
    }

    [Fact]
    public async Task Handler_receives_the_clamped_filters_from_authorization_not_the_raw_llm_filters()
    {
        var rawFilters = new AiSearchFilters("Rahul", "12A", "A", "today", false);
        var clamped = new AiSearchFilters(null, null, null, "today", false);
        var classifier = new FakeClassifier(new AiClassificationResult("en", "StudentAttendance", rawFilters));
        var authz = new FakeAuthz(Allowed("StudentAttendance", clamped));
        var handler = new FakeHandler("StudentAttendance",
            AiSearchResponse.Ok("en", "StudentAttendance", "ok", null, 1, 20, 1, false));
        var service = Build(classifier, authz, [handler], new RecordingAudit(), new FeatureSet(true));

        await service.SearchAsync(new AiSearchRequest("Rahul 12A attendance today", null, null, null), ["school.admin"]);

        authz.LastFilters.Should().BeSameAs(rawFilters);
        handler.LastAuth!.ClampedFilters.Should().BeSameAs(clamped);
        handler.LastAuth.ClampedFilters.StudentName.Should().BeNull();
    }

    [Theory]
    [InlineData(null, null, 1, 20)]
    [InlineData(0, 0, 1, 1)]
    [InlineData(-5, 500, 1, 100)]
    [InlineData(3, 50, 3, 50)]
    public async Task Paging_is_normalised_before_the_handler_sees_it(int? page, int? pageSize, int expPage, int expSize)
    {
        var classifier = new FakeClassifier(new AiClassificationResult("en", "StudentSearch", EmptyFilters));
        var handler = new FakeHandler("StudentSearch",
            AiSearchResponse.Ok("en", "StudentSearch", "ok", null, 1, 20, 0, false));
        var service = Build(classifier, new FakeAuthz(Allowed("StudentSearch")), [handler],
            new RecordingAudit(), new FeatureSet(true));

        await service.SearchAsync(new AiSearchRequest("students named Rahul", page, pageSize, null), ["school.admin"]);

        handler.LastPage.Should().Be(expPage);
        handler.LastPageSize.Should().Be(expSize);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Blank_query_is_rejected_before_classification(string query)
    {
        var classifier = new FakeClassifier(new AiClassificationResult("en", "StudentSearch", EmptyFilters));
        var service = Build(classifier, new FakeAuthz(Allowed("StudentSearch")), [],
            new RecordingAudit(), new FeatureSet(true));

        var result = await service.SearchAsync(new AiSearchRequest(query, null, null, null), ["school.admin"]);

        result.Success.Should().BeFalse();
        result.Error!.Code.Should().Be("InvalidRequest");
        classifier.Calls.Should().Be(0);
    }

    [Fact]
    public async Task Overlong_query_is_rejected_before_classification()
    {
        var classifier = new FakeClassifier(new AiClassificationResult("en", "StudentSearch", EmptyFilters));
        var service = Build(classifier, new FakeAuthz(Allowed("StudentSearch")), [],
            new RecordingAudit(), new FeatureSet(true), options: new AiSearchOptions { MaxQueryLength = 10 });

        var result = await service.SearchAsync(new AiSearchRequest(new string('a', 11), null, null, null), ["school.admin"]);

        result.Success.Should().BeFalse();
        result.Error!.Code.Should().Be("InvalidRequest");
        result.Error.Message.Should().Contain("10");
        classifier.Calls.Should().Be(0);
    }

    [Fact]
    public async Task Successful_search_is_audited_with_caller_identity_question_and_result_count()
    {
        var tenant = new TestTenant();
        var classifier = new FakeClassifier(new AiClassificationResult("hinglish", "HomeworkSearch", EmptyFilters));
        var handler = new FakeHandler("HomeworkSearch",
            AiSearchResponse.Ok("hinglish", "HomeworkSearch", "ok", null, 1, 20, 7, true));
        var audit = new RecordingAudit();
        var service = Build(classifier, new FakeAuthz(Allowed("HomeworkSearch")), [handler],
            audit, new FeatureSet(true), tenant);

        await service.SearchAsync(new AiSearchRequest("Aaj ka homework", null, null, null), ["school.teacher", "staff"]);

        var entry = audit.Entries.Should().ContainSingle().Subject;
        entry.TenantId.Should().Be(tenant.TenantId!.Value);
        entry.UserId.Should().Be(tenant.UserId!.Value);
        entry.Role.Should().Be("school.teacher");
        entry.Question.Should().Be("Aaj ka homework");
        entry.Language.Should().Be("hinglish");
        entry.Intent.Should().Be("HomeworkSearch");
        entry.ResultCount.Should().Be(7);
        entry.Success.Should().BeTrue();
    }

    [Fact]
    public async Task Audit_still_records_when_the_caller_has_no_roles_and_no_tenant_context()
    {
        var tenant = new TestTenant();
        tenant.Set(null, null, false);
        var classifier = new FakeClassifier(new AiClassificationResult("en", "Unsupported", EmptyFilters));
        var audit = new RecordingAudit();
        var service = Build(classifier, new FakeAuthz(Denied()), [], audit,
            new FeatureSet(true), tenant);

        await service.SearchAsync(new AiSearchRequest("hello", null, null, null), []);

        var entry = audit.Entries.Should().ContainSingle().Subject;
        entry.TenantId.Should().Be(Guid.Empty);
        entry.UserId.Should().Be(Guid.Empty);
        entry.Role.Should().BeEmpty();
    }

    [Fact]
    public async Task Handler_exception_is_audited_as_a_failure_and_returned_as_a_SearchFailed_response()
    {
        var tenant = new TestTenant();
        var classifier = new FakeClassifier(new AiClassificationResult("en", "StudentSearch", EmptyFilters));
        var handler = new ThrowingHandler("StudentSearch", new TimeoutException("sql timeout"));
        var audit = new RecordingAudit();
        var service = Build(classifier, new FakeAuthz(Allowed("StudentSearch")), [handler],
            audit, new FeatureSet(true), tenant);

        var result = await service.SearchAsync(
            new AiSearchRequest("students named Rahul", null, null, null), ["school.admin"]);

        handler.Called.Should().BeTrue();
        result.Success.Should().BeFalse("an infra failure must stay inside the response contract");
        result.Error!.Code.Should().Be("SearchFailed");

        var entry = audit.Entries.Should().ContainSingle(
            "every request is audited, including one whose handler blew up").Subject;
        entry.Success.Should().BeFalse();
        entry.ResultCount.Should().Be(0);
        entry.Intent.Should().Be("StudentSearch");
        entry.Question.Should().Be("students named Rahul");
        entry.TenantId.Should().Be(tenant.TenantId!.Value);
    }

    [Fact]
    public async Task Cancellation_is_not_swallowed_by_the_handler_failure_guard()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var classifier = new FakeClassifier(new AiClassificationResult("en", "StudentSearch", EmptyFilters));
        var handler = new ThrowingHandler("StudentSearch", new OperationCanceledException(cts.Token));
        var audit = new RecordingAudit();
        var service = Build(classifier, new FakeAuthz(Allowed("StudentSearch")), [handler],
            audit, new FeatureSet(true));

        var act = () => service.SearchAsync(new AiSearchRequest("students", null, null, null), ["school.admin"], cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        audit.Entries.Should().BeEmpty("an abandoned request has no outcome to record");
    }

    [Fact]
    public async Task Handler_returned_refusal_is_audited_exactly_like_an_orchestrator_level_refusal()
    {
        // Same semantic outcome ("we cannot answer that"), decided in two different places. The audit
        // row must not depend on which one decided it, or WHERE Success = 0 stops selecting refusals.
        static AiSearchService BuildFor(IAiIntentHandler[] handlers, string intent, RecordingAudit audit) =>
            Build(new FakeClassifier(new AiClassificationResult("en", intent, EmptyFilters)),
                new FakeAuthz(Allowed(intent)), handlers, audit, new FeatureSet(true));

        var fromHandler = new RecordingAudit();
        var handler = new FakeHandler("StudentSearch",
            AiSearchResponse.Terminal("en", "Unsupported", "I couldn't understand that.", "no_match"));
        var handlerResult = await BuildFor([handler], "StudentSearch", fromHandler)
            .SearchAsync(new AiSearchRequest("who is Rahul", null, null, null), ["school.admin"]);

        var fromOrchestrator = new RecordingAudit();
        var shortCircuitResult = await BuildFor([], "Unsupported", fromOrchestrator)
            .SearchAsync(new AiSearchRequest("who is Rahul", null, null, null), ["school.admin"]);

        handlerResult.Intent.Should().Be("Unsupported");
        shortCircuitResult.Intent.Should().Be("Unsupported");

        var handlerEntry = fromHandler.Entries.Should().ContainSingle().Subject;
        var orchestratorEntry = fromOrchestrator.Entries.Should().ContainSingle().Subject;
        handlerEntry.Success.Should().Be(orchestratorEntry.Success);
        handlerEntry.Success.Should().BeFalse();
        handlerEntry.ResultCount.Should().Be(orchestratorEntry.ResultCount).And.Be(0);
    }

    [Fact]
    public async Task An_empty_but_authorized_result_set_is_audited_as_unsuccessful()
    {
        var classifier = new FakeClassifier(new AiClassificationResult("en", "StudentSearch", EmptyFilters));
        var handler = new FakeHandler("StudentSearch",
            AiSearchResponse.Ok("en", "StudentSearch", "No students matched.", Array.Empty<int>(), 1, 20, 0, false));
        var audit = new RecordingAudit();
        var service = Build(classifier, new FakeAuthz(Allowed("StudentSearch")), [handler],
            audit, new FeatureSet(true));

        var result = await service.SearchAsync(new AiSearchRequest("students named Zzz", null, null, null), ["school.admin"]);

        result.Success.Should().BeTrue("an empty result set is still a well-formed answer");
        audit.Entries.Should().ContainSingle().Which.Success.Should().BeFalse();
    }

    [Fact]
    public async Task Handler_lookup_is_case_insensitive()
    {
        var classifier = new FakeClassifier(new AiClassificationResult("en", "studentsearch", EmptyFilters));
        var handler = new FakeHandler("StudentSearch",
            AiSearchResponse.Ok("en", "StudentSearch", "ok", null, 1, 20, 0, false));
        var service = Build(classifier, new FakeAuthz(Allowed("StudentSearch")), [handler],
            new RecordingAudit(), new FeatureSet(true));

        var result = await service.SearchAsync(new AiSearchRequest("students", null, null, null), ["school.admin"]);

        handler.Called.Should().BeTrue();
        result.Success.Should().BeTrue();
    }

    // --- Task 12 review Finding I-3: unit coverage for the new conversation-context orchestration ---

    [Fact]
    public async Task An_explicit_language_directive_overrides_a_previously_stored_override_and_is_what_persists_for_the_next_turn()
    {
        var conversationId = Guid.NewGuid();
        var storedContext = new AiConversationContext(null, null, "hi", null, "PersonLookup");
        var contextStore = new ScriptedContextStore(storedContext);
        var classifier = new FakeClassifier(
            new AiClassificationResult("en", "PersonLookup", new AiSearchFilters("Rahul", null, null, null, false), "en"));
        var handler = new EchoLanguageHandler("PersonLookup");
        var service = Build(classifier, new FakeAuthz(Allowed("PersonLookup")), [handler],
            new RecordingAudit(), new FeatureSet(true), contextStore: contextStore);

        var result = await service.SearchAsync(
            new AiSearchRequest("English mein batao, Rahul kaun hai?", null, null, conversationId.ToString()),
            ["school.admin"]);

        result.Language.Should().Be("en", "an explicit directive on this turn must win over a stored 'hi' override");
        contextStore.SaveCalled.Should().BeTrue();
        contextStore.SavedContext!.LanguageOverride.Should().Be(
            "en", "what gets persisted for the NEXT turn must be the fresh directive, not the stale stored 'hi'");
    }

    [Fact]
    public async Task Forbidden_never_persists_a_pending_language_override_even_when_the_classifier_included_one()
    {
        var contextStore = new ScriptedContextStore(null);
        var classifier = new FakeClassifier(new AiClassificationResult("en", "DashboardSummary", EmptyFilters, "hi"));
        // A handler must be registered so the intent is recognized and AuthorizeAsync actually runs
        // (and is then denied) -- otherwise the request would short-circuit as Unsupported before
        // AuthorizeAsync is ever reached, same as Finding I-1's fix elsewhere in this plan.
        var handler = new FakeHandler("DashboardSummary",
            AiSearchResponse.Ok("en", "DashboardSummary", "should never be reached", null, 1, 20, 1, false));
        var service = Build(classifier, new FakeAuthz(Denied()), [handler], new RecordingAudit(),
            new FeatureSet(true), contextStore: contextStore);

        var result = await service.SearchAsync(
            new AiSearchRequest("Hindi mein batao, aaj ka school summary do", null, null, null), ["staff"]);

        result.Intent.Should().Be("Forbidden");
        handler.Called.Should().BeFalse();
        contextStore.SaveCalled.Should().BeFalse(
            "a Forbidden outcome must never touch conversation state, not even to persist a pending language override");
    }

    // --- Task 12 review Finding I-4: unvalidated LLM strings must not reach persistence unclamped ---

    [Fact]
    public async Task A_pathologically_long_classifier_intent_is_clamped_before_persistence_instead_of_crashing()
    {
        var contextStore = new ScriptedContextStore(null);
        var hugeUnsupportedIntent = new string('X', 5000); // no handler implements this -- routes to Unsupported
        var classifier = new FakeClassifier(
            new AiClassificationResult("en", hugeUnsupportedIntent, EmptyFilters, "hi"));
        var service = Build(classifier, new FakeAuthz(Denied()), [], new RecordingAudit(),
            new FeatureSet(true), contextStore: contextStore);

        var act = () => service.SearchAsync(
            new AiSearchRequest("weird prompt-injection-shaped input", null, null, null), ["school.admin"]);

        var result = (await act.Should().NotThrowAsync()).Subject;
        result.Intent.Should().Be("Unsupported");
        contextStore.SaveCalled.Should().BeTrue("a valid 'hi' language directive was present, so a context row is still expected");
        contextStore.SavedContext!.LastIntent.Should().NotBeNull();
        contextStore.SavedContext.LastIntent!.Length.Should().BeLessThanOrEqualTo(60,
            "AiSearchConversation.LastIntent is NVARCHAR(60) -- an over-long value must be clamped before it ever reaches the column");
    }

    [Fact]
    public async Task A_malformed_classifier_language_directive_is_dropped_rather_than_persisted_verbatim()
    {
        var contextStore = new ScriptedContextStore(null);
        var classifier = new FakeClassifier(new AiClassificationResult("en", "Unsupported", EmptyFilters, "not-a-real-language"));
        var service = Build(classifier, new FakeAuthz(Denied()), [], new RecordingAudit(),
            new FeatureSet(true), contextStore: contextStore);

        var act = () => service.SearchAsync(
            new AiSearchRequest("weird prompt-injection-shaped input", null, null, null), ["school.admin"]);

        var result = (await act.Should().NotThrowAsync()).Subject;
        result.Intent.Should().Be("Unsupported");
        contextStore.SaveCalled.Should().BeFalse(
            "only 'en'/'hi' are ever meaningful downstream -- a malformed directive leaves nothing worth persisting, " +
            "so no context row is created (mirroring how a Forbidden outcome with no override leaves state untouched)");
    }
}
