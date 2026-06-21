using Sms.Modules.Academics.Contracts;
using Sms.Shared.Kernel.Data;

namespace Sms.Modules.Academics.Data;

public sealed class LibraryRepository(IDbConnectionFactory factory) : BaseRepository(factory)
{
    public Task<IReadOnlyList<LibraryBookResponse>> ListAsync(DateTime today, CancellationToken ct = default) =>
        QueryInlineAsync<LibraryBookResponse>(
            @"SELECT Id, TenantId, Title, Author, Subject, IssuedTo, DueDate,
                     CASE WHEN Status = 'issued' AND DueDate IS NOT NULL AND DueDate < @today
                          THEN 'overdue' ELSE Status END AS Status
              FROM dbo.LibraryBooks ORDER BY Title", new { today = today.Date }, ct);

    public Task<LibraryBookResponse?> CreateAsync(Guid tenantId, CreateLibraryBookRequest r, CancellationToken ct = default) =>
        QuerySingleProcAsync<LibraryBookResponse>("dbo.LibraryBook_Create", new
        {
            TenantId = tenantId, r.Title, r.Author, r.Subject, r.IssuedTo, DueDate = r.DueDate, Status = r.Status ?? "available"
        }, ct);
}
