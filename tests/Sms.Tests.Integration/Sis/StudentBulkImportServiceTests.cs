using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Sms.Shared.Kernel.Auth;
using Sms.Shared.Kernel.Time;

namespace Sms.Tests.Integration.Sis;

[Collection("sql")]
public class StudentBulkImportServiceTests(SqlServerFixture fx)
{
    private const string Key = "integration-test-signing-key-32-bytes-min!!";

    private WebApplicationFactory<Program> App() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("environment", "Production");
            b.UseSetting("ConnectionStrings:Sql", fx.ConnectionString);
            b.UseSetting("Jwt:SigningKey", Key);
        });

    private static HttpClient TenantClient(WebApplicationFactory<Program> app, Guid tenantId)
    {
        var jwt = new JwtTokenService(
            new JwtOptions { Issuer = "sms", Audience = "sms-apps", SigningKey = Key, AccessTokenMinutes = 15 },
            new SystemClock());
        var token = jwt.IssueAccess(Guid.NewGuid(), tenantId, ["school.owner"], isPlatform: false);
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);
        return client;
    }

    private static object Row(int n, string name, string phone, string email) => new
    {
        row_number = n,
        create_student_request = new
        {
            admission_no = (string?)null, name, gender = "M", grade = "I", section = "A", roll = 0,
            guardian_name = name, guardian_phone = phone, guardian_email = email,
            house = (string?)null, avatar_hue = 0, dob = "2015-01-01", email, address = (string?)null,
        },
        extras_json = "{}",
        transport = (object?)null,
    };

    [Fact]
    public async Task ProcessBatch_creates_all_valid_rows_and_returns_real_counts()
    {
        await using var app = App();
        var client = TenantClient(app, Guid.NewGuid());

        var importId = Guid.NewGuid();
        var resp = await client.PostAsJsonAsync("/v1/students/bulk-import/batch", new
        {
            import_id = importId,
            batch_index = 0,
            rows = new[]
            {
                Row(1, "Aarav Sharma", "9876543210", "aarav@example.com"),
                Row(2, "Aditi Verma", "9876543211", "aditi@example.com"),
            },
        });

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<DataEnvelopeDto<BulkImportBatchResponseDto>>();
        var data = body!.Data;
        data.processed.Should().Be(2);
        data.created.Should().Be(2);
        data.skipped.Should().Be(0);
        data.rows.Should().HaveCount(2);
        data.rows[0].status.Should().Be("created");
        data.rows[0].student_id.Should().NotBeNull();
    }

    [Fact]
    public async Task ProcessBatch_skips_a_row_missing_a_required_field_without_failing_the_batch()
    {
        await using var app = App();
        var client = TenantClient(app, Guid.NewGuid());

        var badRow = Row(1, "", "9876543212", "blank-name@example.com"); // blank name -> should be skipped
        var goodRow = Row(2, "Rahul Gupta", "9876543213", "rahul@example.com");

        var resp = await client.PostAsJsonAsync("/v1/students/bulk-import/batch", new
        {
            import_id = Guid.NewGuid(),
            batch_index = 0,
            rows = new[] { badRow, goodRow },
        });

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<DataEnvelopeDto<BulkImportBatchResponseDto>>();
        var data = body!.Data;
        data.created.Should().Be(1);
        data.skipped.Should().Be(1);
        data.rows.First(r => r.row_number == 1).status.Should().Be("skipped");
        data.rows.First(r => r.row_number == 1).error.Should().NotBeNullOrEmpty();
        data.rows.First(r => r.row_number == 2).status.Should().Be("created");
    }

    [Fact]
    public async Task ProcessBatch_replaying_the_same_import_and_batch_index_never_creates_duplicates()
    {
        await using var app = App();
        var tenantId = Guid.NewGuid();
        var client = TenantClient(app, tenantId);
        var importId = Guid.NewGuid();
        var payload = new
        {
            import_id = importId,
            batch_index = 0,
            rows = new[] { Row(1, "Neha Singh", "9876543214", "neha@example.com") },
        };

        var first = await client.PostAsJsonAsync("/v1/students/bulk-import/batch", payload);
        var second = await client.PostAsJsonAsync("/v1/students/bulk-import/batch", payload);

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        second.StatusCode.Should().Be(HttpStatusCode.OK);
        var firstBody = await first.Content.ReadFromJsonAsync<DataEnvelopeDto<BulkImportBatchResponseDto>>();
        var secondBody = await second.Content.ReadFromJsonAsync<DataEnvelopeDto<BulkImportBatchResponseDto>>();
        secondBody!.Data.rows[0].student_id.Should().Be(firstBody!.Data.rows[0].student_id);

        // Confirm no duplicate row was actually inserted: list this tenant's grade "I" students
        // (the /v1/students `q` search only matches name/admission-no/class-label, not email,
        // so filter client-side by email on the tenant-scoped list rather than via `q=`).
        var check = await client.GetAsync("/v1/students?grade=I");
        var students = await check.Content.ReadFromJsonAsync<StudentListEnvelopeDto>();
        students!.data.Count(s => s.email == "neha@example.com").Should().Be(1);
    }

    private sealed record DataEnvelopeDto<T>(T Data);
    private sealed record BulkImportRowResponseDto(int row_number, string? student_id, string status, string? error);
    private sealed record BulkImportBatchResponseDto(
        Guid import_id, int batch_index, int processed, int created, int skipped, int transport_pending,
        List<BulkImportRowResponseDto> rows);
    private sealed record StudentListItemDto(string? email);
    private sealed record StudentListEnvelopeDto(List<StudentListItemDto> data, string? next_cursor);
}
