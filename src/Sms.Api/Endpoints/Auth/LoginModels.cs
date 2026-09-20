// Backward-compatible re-exports — DTOs live in Sms.Application.DTOs.Auth.
global using LoginRequest = Sms.Application.DTOs.Auth.LoginRequest;
global using TokenResponse = Sms.Application.DTOs.Auth.TokenResponse;
global using RefreshRequest = Sms.Application.DTOs.Auth.RefreshRequest;
global using OtpRequest = Sms.Application.DTOs.Auth.OtpRequest;
global using OtpVerifyRequest = Sms.Application.DTOs.Auth.OtpVerifyRequest;
global using SetPasswordRequest = Sms.Application.DTOs.Auth.SetPasswordRequest;
global using ForgotPasswordRequest = Sms.Application.DTOs.Auth.ForgotPasswordRequest;
global using ResetPasswordRequest = Sms.Application.DTOs.Auth.ResetPasswordRequest;

namespace Sms.Api.Endpoints.Auth;
