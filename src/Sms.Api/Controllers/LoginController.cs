using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Sms.Application.DTOs.Auth;
using Sms.Application.Services.Auth;

namespace Sms.Api.Controllers;

[Route("v1/auth")]
[EnableRateLimiting("auth")]
public sealed class LoginController(IAuthService auth) : ApiControllerBase
{
    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login([FromBody] LoginRequest req, CancellationToken ct) =>
        FromResult(await auth.LoginAsync(req, ct));

    [HttpPost("refresh")]
    [AllowAnonymous]
    public async Task<IActionResult> Refresh([FromBody] RefreshRequest req, CancellationToken ct) =>
        FromResult(await auth.RefreshAsync(req, ct));

    [HttpPost("otp/request")]
    [AllowAnonymous]
    public async Task<IActionResult> RequestOtp([FromBody] OtpRequest req, CancellationToken ct) =>
        FromResult(await auth.RequestOtpAsync(req, ct));

    [HttpPost("otp/verify")]
    [AllowAnonymous]
    public async Task<IActionResult> VerifyOtp([FromBody] OtpVerifyRequest req, CancellationToken ct) =>
        FromResult(await auth.VerifyOtpAsync(req, ct));

    [HttpPost("password/forgot")]
    [AllowAnonymous]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest req, CancellationToken ct) =>
        FromResult(await auth.ForgotPasswordAsync(req, ct));

    [HttpPost("password/reset")]
    [AllowAnonymous]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest req, CancellationToken ct) =>
        FromResult(await auth.ResetPasswordAsync(req, ct));

    [HttpPost("set-password")]
    [Authorize]
    public async Task<IActionResult> SetPassword([FromBody] SetPasswordRequest req, CancellationToken ct) =>
        FromResult(await auth.SetPasswordAsync(req, ct));

    [HttpGet("me")]
    [Authorize]
    public IActionResult Me() => FromResult(auth.GetMe(User));

    [HttpPost("logout")]
    [AllowAnonymous]
    public async Task<IActionResult> Logout([FromBody] RefreshRequest req, CancellationToken ct) =>
        FromResult(await auth.LogoutAsync(req, ct));
}
