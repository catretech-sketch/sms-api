using Sms.Modules.Staffing.Contracts;
using Sms.Shared.Kernel.Data;

namespace Sms.Modules.Staffing.Profile;

public sealed class ProfileRepository(IDbConnectionFactory factory) : BaseRepository(factory)
{
    public Task<IReadOnlyList<StaffDocumentResponse>> ListForUserAsync(
        Guid tenantId, Guid userId, CancellationToken ct = default) =>
        QueryProcAsync<StaffDocumentResponse>(
            "dbo.StaffDocuments_ListForUser", new { TenantId = tenantId, UserId = userId }, ct);

    public Task<IReadOnlyList<StaffDocumentResponse>> ListForStaffAsync(
        Guid tenantId, Guid staffId, CancellationToken ct = default) =>
        QueryInlineAsync<StaffDocumentResponse>(
            "SELECT Id, Label, Value, Ok FROM dbo.StaffDocuments " +
            "WHERE TenantId = @tenantId AND StaffId = @staffId ORDER BY CreatedAt",
            new { tenantId, staffId }, ct);

    public Task<StaffDocumentResponse?> CreateAsync(
        Guid tenantId, Guid staffId, CreateStaffDocumentRequest r, CancellationToken ct = default) =>
        QuerySingleProcAsync<StaffDocumentResponse>("dbo.StaffDocument_Create",
            new { TenantId = tenantId, StaffId = staffId, r.Label, r.Value, r.Ok }, ct);

    public Task<StaffDocumentResponse?> UpdateAsync(
        Guid tenantId, Guid staffId, Guid docId, UpdateStaffDocumentRequest r, CancellationToken ct = default) =>
        QuerySingleProcAsync<StaffDocumentResponse>("dbo.StaffDocument_Update",
            new { Id = docId, TenantId = tenantId, StaffId = staffId, r.Label, r.Value, r.Ok }, ct);

    public async Task<bool> DeleteAsync(Guid tenantId, Guid staffId, Guid docId, CancellationToken ct = default) =>
        await ExecuteInlineAsync(
            "DELETE FROM dbo.StaffDocuments WHERE Id = @docId AND StaffId = @staffId AND TenantId = @tenantId",
            new { docId, staffId, tenantId }, ct) > 0;
}
