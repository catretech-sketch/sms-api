using Sms.Modules.Academics.Contracts;
using Sms.Shared.Kernel.Data;

namespace Sms.Modules.Academics.Data;

public sealed class PersonExtrasRepository(IDbConnectionFactory factory) : BaseRepository(factory)
{
    public Task<PersonExtrasResponse?> GetAsync(string personType, Guid personId, CancellationToken ct = default) =>
        QuerySingleProcAsync<PersonExtrasResponse>("dbo.PersonExtras_Get", new
        {
            PersonType = personType,
            PersonId = personId,
        }, ct);

    public Task<PersonExtrasResponse?> UpsertAsync(
        Guid tenantId, string personType, Guid personId, string extrasJson, CancellationToken ct = default) =>
        QuerySingleProcAsync<PersonExtrasResponse>("dbo.PersonExtras_Upsert", new
        {
            TenantId = tenantId,
            PersonType = personType,
            PersonId = personId,
            ExtrasJson = extrasJson,
        }, ct);
}
