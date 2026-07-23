namespace Sms.Shared.Kernel.Auth;

/// Base URL of the frontend app, used to build clickable onboarding/reset links in emails.
public sealed class FrontendOptions
{
    public string BaseUrl { get; init; } = "http://localhost:5173";
}
