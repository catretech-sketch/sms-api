using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using OpenTelemetry.Trace;
using Serilog;
using Sms.Api.Endpoints;
using Sms.Migrations;
using Sms.Shared.Kernel.Auth;
using Sms.Shared.Kernel.Data;
using Sms.Shared.Kernel.Http;
using Sms.Shared.Kernel.Tenancy;
using Sms.Shared.Kernel.Time;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, cfg) => cfg.ReadFrom.Configuration(ctx.Configuration).WriteTo.Console());

var conn = builder.Configuration.GetConnectionString("Sql")!;
var jwtOptions = builder.Configuration.GetSection("Jwt").Get<JwtOptions>()!;

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
builder.Services.AddSingleton<IOtpSender, ConsoleOtpSender>();

builder.Services.AddScoped<ITenantContext, TenantContext>();
builder.Services.AddScoped<IDbConnectionFactory>(sp =>
    new SqlConnectionFactory(conn, sp.GetRequiredService<ITenantContext>()));
builder.Services.AddScoped<AuthRepository>();
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
builder.Services.AddAuthorization();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddOpenTelemetry()
    .WithTracing(t => t.AddAspNetCoreInstrumentation().AddConsoleExporter());

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    MigrationRunner.Run(conn); // tables + RLS + procs on startup in dev
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseSerilogRequestLogging();
app.UseAuthentication();
app.UseMiddleware<TenantResolutionMiddleware>(); // after auth: needs ClaimsPrincipal
app.UseAuthorization();

app.MapHealth();
app.MapAuth();
Sms.Modules.Tenancy.ModuleEndpoints.MapTenancyModule(app);

app.Run();

public partial class Program { }
