using FluentValidation;
using Sms.Application.DTOs.Auth;

namespace Sms.Application.Validators.Auth;

public sealed class ResetPasswordRequestValidator : AbstractValidator<ResetPasswordRequest>
{
    public ResetPasswordRequestValidator()
    {
        RuleFor(x => x.Identifier).NotEmpty();
        // Code and password strength are validated in AuthService to preserve legacy status codes.
    }
}

public sealed class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(x => x).Must(r =>
                (!string.IsNullOrWhiteSpace(r.Email)
                 || !string.IsNullOrWhiteSpace(r.Phone)
                 || !string.IsNullOrWhiteSpace(r.StudentId))
                && r.Password is not null)
            .WithMessage("email, phone, or student_id and password required");
    }
}
