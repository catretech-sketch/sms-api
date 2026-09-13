using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sms.Api.Filters;
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
    // [SkipModelValidation]: BulkImportRowRequest nests the shared CreateStudentRequest, whose
    // Name (and other reference-type properties) is non-nullable. Without this, [ApiController]'s
    // automatic model-state validation rejects the WHOLE batch with 400 the instant any single
    // row has a null Name — before StudentBulkImportService's own per-row guard ever runs. See
    // Sms.Api/Filters/SkipModelValidationAttribute.cs for the full rationale.
    [HttpPost("batch")]
    [SkipModelValidation]
    public async Task<IActionResult> Batch([FromBody] BulkImportBatchRequest? req, CancellationToken ct)
    {
        // Defence in depth behind SkipModelValidationAttribute: that filter now preserves body
        // binding failures so ModelStateInvalidFilter returns 400 before we get here, but if a
        // body ever fails to bind without a recorded ModelState error (empty body, an
        // application/json request with no content), `req` is null and dereferencing it in the
        // service would be an unhandled NullReferenceException — a 500 for the whole batch.
        if (req is null)
            return BadRequestResult("request body is required");

        return FromResult(await bulkImport.ProcessBatchAsync(req, ct));
    }
}
