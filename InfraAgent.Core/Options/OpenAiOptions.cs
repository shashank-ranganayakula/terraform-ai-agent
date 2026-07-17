namespace InfraAgent.Core.Options;

public sealed class OpenAiOptions
{
    public string? ApiKey { get; init; }

    public string Model { get; init; } = "gpt-4.1-mini";

    public string? BaseUrl { get; init; }
}
