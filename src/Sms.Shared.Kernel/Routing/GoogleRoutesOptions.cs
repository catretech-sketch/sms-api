namespace Sms.Shared.Kernel.Routing;

public sealed class GoogleRoutesOptions
{
    public const string SectionName = "GoogleRoutes";
    public string ApiKey { get; set; } = "";
    public string BaseUrl { get; set; } = "https://routes.googleapis.com";
    public int TimeoutSeconds { get; set; } = 8;
    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey);
}
