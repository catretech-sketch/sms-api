using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sms.Application.Services.Sis;
using Sms.Modules.Sis.Contracts;
using Sms.Shared.Kernel.Authz;

namespace Sms.Api.Controllers;

/// Bulk Add Students — reuses the exact same CreateStudentAsync/UpsertPersonExtrasAsync/
/// StudentTransportService.SetAsync calls as single Add, looped per row within an idempotent,
/// 200-row-at-a-time batch. Policies.Principal (not just staff) because this endpoint also
/// writes person extras on the caller's behalf, matching PersonExtrasController's PUT policy —
/// a plainly-staffed caller must not gain extras-write access merely by going through bulk import.
[Route("v1/students/bulk-import")]
[Authorize(Policy = Policies.Principal)]
public sealed class StudentBulkImportController(IStudentBulkImportService bulkImport) : ApiControllerBase
{
    [HttpPost("batch")]
    public async Task<IActionResult> Batch([FromBody] BulkImportBatchRequest req, CancellationToken ct) =>
        FromResult(await bulkImport.ProcessBatchAsync(req, ct));
}
