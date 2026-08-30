namespace Sms.Shared.Kernel.AiSearch;

public sealed class AiSearchOptions
{
    public const string SectionName = "AiSearch";
    public string ApiKey { get; set; } = "";
    public string Model { get; set; } = "claude-haiku-4-5-20251001";
    public string BaseUrl { get; set; } = "https://api.anthropic.com";
    public int TimeoutSeconds { get; set; } = 8;
    public int MaxQueryLength { get; set; } = 300;
    public int ConversationContextTtlMinutes { get; set; } = 10;
    public int ConversationContextAbsoluteMaxMinutes { get; set; } = 30;
    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey);
}
