namespace Sms.Modules.Academics.Contracts;

public sealed record PersonExtrasResponse(
    Guid Id, Guid TenantId, string PersonType, Guid PersonId, string ExtrasJson, DateTime UpdatedAt);

public sealed record UpsertPersonExtrasRequest(string ExtrasJson);
