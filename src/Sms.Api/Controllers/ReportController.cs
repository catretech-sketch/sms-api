using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sms.Application.Services.Tenancy;

namespace Sms.Api.Controllers;

[Route("v1")]
[Authorize(Policy = "platform")]
public sealed class ReportController(ITenancyService tenancy) : ApiControllerBase
{
    [HttpGet("reports/revenue")]
    public async Task<IActionResult> Revenue(CancellationToken ct) =>
        OkData(await tenancy.GetRevenueReportAsync(ct));

    [HttpGet("reports/clients.csv")]
    public async Task<IActionResult> ClientsCsv(CancellationToken ct)
    {
        var csv = await tenancy.ExportClientsCsvAsync(ct);
        Response.Headers.ContentDisposition = "attachment; filename=\"catre-clients.csv\"";
        return File(Encoding.UTF8.GetBytes(csv), "text/csv");
    }
}
