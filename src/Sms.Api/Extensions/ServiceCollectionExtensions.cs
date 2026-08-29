using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using OpenTelemetry.Trace;
using Serilog;
using Sms.Api.Http;
using Sms.Api.Hubs;
using Sms.Api.Services;
using Sms.Api.Swagger;
using Sms.Application.Services.AiSearch;
using Sms.Application.Services.AiSearch.Handlers;
using Sms.Application.Services.Realtime;
using Sms.Application.Services.Transport;
using Sms.Application;
using Sms.Infrastructure;
using Sms.Migrations;
using Sms.Modules.Academics;
using Sms.Modules.AiSearch;
using Sms.Modules.AiSearch.Data;
using Sms.Modules.Attendance;
using Sms.Modules.Comms;
using Sms.Modules.Finance;
using Sms.Modules.Hostel;
using Sms.Modules.Reporting;
using Sms.Modules.Sis;
using Sms.Modules.Sports;
using Sms.Modules.Staffing;
using Sms.Modules.Tenancy;
using Sms.Modules.Transport;
using Sms.Shared.Kernel.AiSearch;
using Sms.Shared.Kernel.Auth;
using Sms.Shared.Kernel.Authz;
using Sms.Shared.Kernel.Configuration;
using Sms.Shared.Kernel.Data;
using Sms.Shared.Kernel.Http;
using Sms.Shared.Kernel.Payments;
using Sms.Shared.Kernel.Results;
using Sms.Shared.Kernel.Tenancy;
using Sms.Shared.Kernel.Time;

namespace Sms.Api.Extensions;

public static class ServiceCollectionExtensions
{
    public static WebApplicationBuilder ConfigureSmsServices(this WebApplicationBuilder builder)
    {
        builder.Host.UseSerilog((ctx, cfg) => cfg.ReadFrom.Configuration(ctx.Configuration).WriteTo.Console());

        var conn = builder.Configuration.GetConnectionString("Sql");
        var jwtOptions = builder.Configuration.GetSection("Jwt").Get<JwtOptions>() ?? new JwtOptions();
        SecretsValidator.Validate(jwtOptions.SigningKey, conn);

        DapperSnakeCaseConfig.Apply();

        builder.Services.ConfigureHttpJsonOptions(o =>
        {
            o.SerializerOptions.PropertyNamingPolicy = new SnakeCaseNamingPolicy();
            o.SerializerOptions.DictionaryKeyPolicy = new SnakeCaseNamingPolicy();
            o.SerializerOptions.PropertyNameCaseInsensitive = true;
        });

        builder.Services.AddControllers()
            .AddJsonOptions(o =>
            {
                o.JsonSerializerOptions.PropertyNamingPolicy = new SnakeCaseNamingPolicy();
                o.JsonSerializerOptions.DictionaryKeyPolicy = new SnakeCaseNamingPolicy();
                // Accept both snake_case and PascalCase from clients (e.g. Status vs status).
                o.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
            });

        // Keep invalid-body responses in the same {error:{code,message}} envelope as before controllers.
        builder.Services.Configure<Microsoft.AspNetCore.Mvc.ApiBehaviorOptions>(o =>
        {
            o.InvalidModelStateResponseFactory = ctx =>
            {
                var message = ctx.ModelState.Values
                    .SelectMany(v => v.Errors)
                    .Select(e => string.IsNullOrWhiteSpace(e.ErrorMessage) ? e.Exception?.Message : e.ErrorMessage)
                    .FirstOrDefault(m => !string.IsNullOrWhiteSpace(m))
                    ?? "invalid request";
                return new Microsoft.AspNetCore.Mvc.BadRequestObjectResult(
                    ErrorEnvelope.From(new Error("invalid_request", message)));
            };
        });

        builder.Services.AddSingleton(jwtOptions);
        builder.Services.AddSingleton<IClock, SystemClock>();
        builder.Services.AddSingleton<IJwtTokenService, JwtTokenService>();
        builder.Services.AddSingleton<IPasswordHasher, PasswordHasher>();
        builder.Services.AddSingleton(builder.Configuration.GetSection("Smtp").Get<SmtpOptions>() ?? new SmtpOptions());
        builder.Services.AddSingleton(builder.Configuration.GetSection("Frontend").Get<FrontendOptions>() ?? new FrontendOptions());
        builder.Services.AddSingleton<IEmailQueue, EmailQueue>();
        builder.Services.AddSingleton<IEmailSender, SmtpEmailSender>();
        builder.Services.AddSingleton<ISmsSender, LoggingSmsSender>();
        builder.Services.AddSingleton(sp => new EmailOtpSender(
            sp.GetRequiredService<IEmailQueue>(),
            sp.GetRequiredService<ILogger<EmailOtpSender>>(),
            builder.Environment.IsDevelopment()));
        builder.Services.AddSingleton<ConsoleOtpSender>();
        builder.Services.AddSingleton<IOtpSender, ChannelOtpSender>();
        builder.Services.AddHostedService<EmailDispatchWorker>();
        builder.Services.AddSingleton<IPaymentGateway, StubPaymentGateway>();
        builder.Services.Configure<RazorpayOptions>(builder.Configuration.GetSection(RazorpayOptions.SectionName));
        builder.Services.AddHttpClient("razorpay");
        builder.Services.AddSingleton<IRazorpayGateway, RazorpayGateway>();
        builder.Services.Configure<AiSearchOptions>(builder.Configuration.GetSection(AiSearchOptions.SectionName));
        builder.Services.AddHttpClient("claude", (sp, client) =>
        {
            var aiOptions = sp.GetRequiredService<IOptions<AiSearchOptions>>().Value;
            client.BaseAddress = new Uri(aiOptions.BaseUrl);
        });
        builder.Services.AddScoped<IAiClassificationClient>(sp =>
            new AiClassificationClient(
                sp.GetRequiredService<IHttpClientFactory>().CreateClient("claude"),
                sp.GetRequiredService<IOptions<AiSearchOptions>>()));
        builder.Services.AddAiSearchModule();
        builder.Services.AddScoped<IAiSearchAuditService, AiSearchAuditService>();
        builder.Services.AddScoped<IAiSearchAuthorizationService, AiSearchAuthorizationService>();
        builder.Services.AddSingleton<IAiAnswerTemplateService, AiAnswerTemplateService>();
        builder.Services.AddScoped<AiAttendanceAggregateRepository>();
        builder.Services.AddScoped<IAiIntentHandler, DailyAttendanceSummaryHandler>();
        builder.Services.AddScoped<IAiIntentHandler, DashboardSummaryHandler>();
        builder.Services.AddScoped<IAiIntentHandler, ClassAttendanceHandler>();
        builder.Services.AddScoped<IAiIntentHandler, SectionAttendanceHandler>();
        builder.Services.AddScoped<IAiIntentHandler, StudentAttendanceHandler>();
        builder.Services.AddScoped<IAiIntentHandler, TeacherAttendanceHandler>();
        builder.Services.AddScoped<IAiIntentHandler, StaffAttendanceHandler>();
        builder.Services.AddScoped<IAiIntentHandler, StudentSearchHandler>();
        builder.Services.AddScoped<IAiIntentHandler, StudentDetailsHandler>();
        builder.Services.AddScoped<IAiIntentHandler, TeacherSearchHandler>();
        builder.Services.AddScoped<IAiIntentHandler, StaffSearchHandler>();
        builder.Services.AddScoped<IAiIntentHandler, UpcomingExamSearchHandler>();
        builder.Services.AddScoped<IAiIntentHandler, TestSearchHandler>();
        builder.Services.AddScoped<IAiIntentHandler, HomeworkSearchHandler>();
        builder.Services.AddScoped<IAiIntentHandler, SubjectSearchHandler>();
        builder.Services.AddScoped<IAiIntentHandler, BusLocationSearchHandler>();

        builder.Services.AddScoped<ITenantContext, TenantContext>();
        builder.Services.AddScoped<ITenantPlan, TenantPlan>();
        builder.Services.AddScoped<TenantPlanRepository>();
        builder.Services.AddScoped<ITenantFeatureSet, TierFeatureSet>();
        builder.Services.AddScoped<IDbConnectionFactory>(sp =>
            new SqlConnectionFactory(conn!, sp.GetRequiredService<ITenantContext>()));
        builder.Services.AddScoped<AuthRepository>();
        builder.Services.AddScoped<UserProvisioningRepository>();
        builder.Services.AddScoped<IRefreshTokenStore, RefreshTokenStore>();

        builder.Services.AddInfrastructureDaos();
        builder.Services.AddApplicationServices();

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
                o.Events = new JwtBearerEvents
                {
                    OnMessageReceived = context =>
                    {
                        var accessToken = context.Request.Query["access_token"];
                        var path = context.HttpContext.Request.Path;
                        if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs"))
                            context.Token = accessToken;
                        return Task.CompletedTask;
                    }
                };
                o.Events = new JwtBearerEvents
                {
                    OnMessageReceived = ctx =>
                    {
                        var accessToken = ctx.Request.Query["access_token"].ToString();
                        if (!string.IsNullOrEmpty(accessToken)
                            && ctx.HttpContext.Request.Path.StartsWithSegments("/hubs"))
                            ctx.Token = accessToken;
                        return Task.CompletedTask;
                    }
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
        builder.Services.AddStudentBusModule();
        builder.Services.AddHostelModule();
        builder.Services.AddSportsModule();
        builder.Services.AddCommsModule();
        builder.Services.AddReportingModule();

        builder.Services.AddSignalR().AddJsonProtocol(o =>
        {
            o.PayloadSerializerOptions.PropertyNamingPolicy = new SnakeCaseNamingPolicy();
            o.PayloadSerializerOptions.PropertyNameCaseInsensitive = true;
        });
        builder.Services.AddScoped<ILiveBroadcaster, SignalRLiveBroadcaster>();
        builder.Services.AddScoped<ITransportFleetBroadcaster, TransportFleetBroadcaster>();

        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen(c =>
        {
            foreach (var (key, title) in ApiAudienceMap.Apps)
                c.SwaggerDoc(key, new OpenApiInfo { Title = title, Version = "v1" });
            c.DocInclusionPredicate((docName, api) => ApiAudienceMap.AppsFor(api.RelativePath).Contains(docName));
            c.TagActionsBy(api => [ApiAudienceMap.TagFor(api.RelativePath)]);
        });

        builder.Services.AddOpenTelemetry()
            .WithTracing(t => t.AddAspNetCoreInstrumentation().AddConsoleExporter());

        builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
        builder.Services.AddProblemDetails();

        builder.Services.AddHealthChecks().AddCheck("sql", new SqlHealthCheck(conn!), tags: ["ready"]);

        var corsOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
        builder.Services.AddCors(o => o.AddPolicy("sms", p =>
        {
            if (corsOrigins.Length > 0)
                p.WithOrigins(corsOrigins).AllowAnyHeader().AllowAnyMethod().AllowCredentials();
            else
                p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
        }));

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

        return builder;
    }
}
