namespace InfraAgent.Core.Options;

public sealed class OpenAiOptions
{
    public string ApiKey { get; set; } = "";

    public string BaseUrl { get; set; } = "";

    public string Model { get; set; } = "gpt-4.1-mini";
}