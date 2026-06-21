using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using OpenTelemetry.Trace;
using Serilog;
using Sms.Api.Endpoints;
using Sms.Api.Swagger;
using Sms.Api.Http;
using Sms.Migrations;
using Sms.Modules.Academics;
using Sms.Modules.Attendance;
using Sms.Modules.Comms;
using Sms.Modules.Finance;
using Sms.Modules.Reporting;
using Sms.Modules.Sis;
using Sms.Modules.Staffing;
using Sms.Modules.Tenancy;
using Sms.Modules.Transport;
using Sms.Shared.Kernel.Auth;
using Sms.Shared.Kernel.Authz;
using Sms.Shared.Kernel.Configuration;
using Sms.Shared.Kernel.Data;
using Sms.Shared.Kernel.Http;
using Sms.Shared.Kernel.Payments;
using Sms.Shared.Kernel.Results;
using Sms.Shared.Kernel.Tenancy;
using Sms.Shared.Kernel.Time;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, cfg) => cfg.ReadFrom.Configuration(ctx.Configuration).WriteTo.Console());

var conn = builder.Configuration.GetConnectionString("Sql");
var jwtOptions = builder.Configuration.GetSection("Jwt").Get<JwtOptions>() ?? new JwtOptions();

// Fail-fast: refuse to boot with a missing connection string or a missing/placeholder/weak signing key.
SecretsValidator.Validate(jwtOptions.SigningKey, conn);

DapperSnakeCaseConfig.Apply();

builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.PropertyNamingPolicy = new SnakeCaseNamingPolicy();
    o.SerializerOptions.DictionaryKeyPolicy = new SnakeCaseNamingPolicy();
});

builder.Services.AddSingleton(jwtOptions);
builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddSingleton<IJwtTokenService, JwtTokenService>();
builder.Services.AddSingleton<IPasswordHasher, PasswordHasher>();
builder.Services.AddSingleton(builder.Configuration.GetSection("Smtp").Get<SmtpOptions>() ?? new SmtpOptions());
builder.Services.AddSingleton<IEmailQueue, EmailQueue>();
builder.Services.AddSingleton<IEmailSender, SmtpEmailSender>();
builder.Services.AddSingleton(sp => new EmailOtpSender(
    sp.GetRequiredService<IEmailQueue>(),
    sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<EmailOtpSender>>(),
    builder.Environment.IsDevelopment())); // dev: log OTP code to console (no SMTP needed)
builder.Services.AddSingleton<ConsoleOtpSender>();
builder.Services.AddSingleton<IOtpSender, ChannelOtpSender>(); // email -> SMTP (worker), sms -> console stub
builder.Services.AddHostedService<EmailDispatchWorker>();
builder.Services.AddSingleton<IPaymentGateway, StubPaymentGateway>();

builder.Services.AddScoped<ITenantContext, TenantContext>();
builder.Services.AddScoped<ITenantPlan, TenantPlan>();
builder.Services.AddScoped<TenantPlanRepository>();
builder.Services.AddScoped<ITenantFeatureSet, TierFeatureSet>();
builder.Services.AddScoped<IDbConnectionFactory>(sp =>
    new SqlConnectionFactory(conn!, sp.GetRequiredService<ITenantContext>()));
builder.Services.AddScoped<AuthRepository>();
builder.Services.AddScoped<UserProvisioningRepository>();
builder.Services.AddScoped<IRefreshTokenStore, RefreshTokenStore>();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.MapInboundClaims = false;
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = jwtOptions.Issuer,
            ValidAudience = jwtOptions.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.SigningKey)),
            ValidateIssuer = true, ValidateAudience = true, ValidateLifetime = true,
            RoleClaimType = "role", NameClaimType = "sub"
        };
    });
builder.Services.AddSmsAuthorization();
builder.Services.AddTenancyModule();
builder.Services.AddSisModule();
builder.Services.AddStaffingModule();
builder.Services.AddAcademicsModule();
builder.Services.AddFinanceModule();
builder.Services.AddAttendanceModule();
builder.Services.AddTransportModule();
builder.Services.AddBusModule();
builder.Services.AddCommsModule();
builder.Services.AddReportingModule();
builder.Services.AddEndpointsApiExplorer();
// One Swagger document per frontend app (Catre Admin / School Admin / Teacher / Student / Staff).
// An endpoint is included in every app that consumes it (ApiAudienceMap); tag = its resource.
builder.Services.AddSwaggerGen(c =>
{
    foreach (var (key, title) in ApiAudienceMap.Apps)
        c.SwaggerDoc(key, new OpenApiInfo { Title = title, Version = "v1" });
    c.DocInclusionPredicate((docName, api) => ApiAudienceMap.AppsFor(api.RelativePath).Contains(docName));
    c.TagActionsBy(api => [ApiAudienceMap.TagFor(api.RelativePath)]);
});

builder.Services.AddOpenTelemetry()
    .WithTracing(t => t.AddAspNetCoreInstrumentation().AddConsoleExporter());

// Standard error-envelope handler for any unhandled exception (no internals leaked).
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

// Readiness: real DB probe on /health/ready (tagged "ready"); /health stays a static liveness check.
builder.Services.AddHealthChecks().AddCheck("sql", new SqlHealthCheck(conn!), tags: ["ready"]);

// CORS — allowed origins from config (dev: the five frontend localhost ports; prod: env).
var corsOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(o => o.AddPolicy("sms", p =>
{
    if (corsOrigins.Length > 0)
        p.WithOrigins(corsOrigins).AllowAnyHeader().AllowAnyMethod().AllowCredentials();
    else
        p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
}));

// Rate limiting — global per-IP sliding window + a stricter fixed window on /v1/auth/*. Config-tunable.
var globalPermit = builder.Configuration.GetValue<int?>("RateLimiting:GlobalPermitPerWindow") ?? 100;
var globalWindow = builder.Configuration.GetValue<int?>("RateLimiting:GlobalWindowSeconds") ?? 10;
var authPermit = builder.Configuration.GetValue<int?>("RateLimiting:AuthPermitPerMinute") ?? 5;
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(http =>
        RateLimitPartition.GetSlidingWindowLimiter(
            http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new SlidingWindowRateLimiterOptions
            {
                PermitLimit = globalPermit, Window = TimeSpan.FromSeconds(globalWindow),
                SegmentsPerWindow = 2, QueueLimit = 0
            }));
    o.AddPolicy("auth", http =>
        RateLimitPartition.GetFixedWindowLimiter(
            http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = authPermit, Window = TimeSpan.FromMinutes(1), QueueLimit = 0
            }));
    o.OnRejected = async (ctx, ct) =>
    {
        ctx.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        await ctx.HttpContext.Response.WriteAsJsonAsync(
            ErrorEnvelope.From(new Error("rate_limited", "Too many requests.")), ct);
    };
});

var app = builder.Build();

app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    MigrationRunner.Run(conn!); // dev convenience; prod migrates via the Sms.Migrations CLI in CI/CD
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        foreach (var (key, title) in ApiAudienceMap.Apps)
            c.SwaggerEndpoint($"/swagger/{key}/swagger.json", title);
    });
}

// Bootstrap the first Catre platform admin (idempotent; no-ops once one exists).
await Sms.Api.Auth.PlatformAdminSeeder.RunAsync(app);

// Refresh the current-month platform metrics snapshot (idempotent; feeds dashboard trend + churn).
await Sms.Api.Metrics.MetricsSnapshotWriter.RunAsync(app);

app.UseSerilogRequestLogging();
app.UseCors("sms");
app.UseRateLimiter();
app.UseAuthentication();
app.UseMiddleware<TenantResolutionMiddleware>(); // after auth: needs ClaimsPrincipal
app.UseMiddleware<BillingStateMiddleware>();       // after tenant resolution: needs ITenantPlan
app.UseAuthorization();

app.MapHealth();
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = r => r.Tags.Contains("ready"),
    ResponseWriter = async (ctx, report) =>
    {
        ctx.Response.ContentType = "application/json";
        var status = report.Status == HealthStatus.Healthy ? "ready" : "unavailable";
        await ctx.Response.WriteAsJsonAsync(new { status });
    }
});
app.MapAuth();
app.MapUsers();
Sms.Modules.Tenancy.ModuleEndpoints.MapTenancyModule(app);
app.MapSisModule();
app.MapStaffingModule();
app.MapAcademicsModule();
app.MapFinanceModule();
app.MapAttendanceModule();
app.MapTransportModule();
app.MapBusModule();
app.MapCommsModule();
app.MapReportingModule();

app.Run();

public partial class Program { }
