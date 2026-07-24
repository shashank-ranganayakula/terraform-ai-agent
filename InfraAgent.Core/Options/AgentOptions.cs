namespace InfraAgent.Core.Options;

public sealed class AgentOptions
{
    public string[] AllowedEc2InstanceTypes { get; init; } = ["t3.micro", "t3.small", "t3.medium"];

    public int MaxRepairAttempts { get; init; } = 3;

    public int MaxPromptCharacters { get; init; } = 2000;

    public int S3BucketPreflightTimeoutSeconds { get; init; } = 5;

    public string WorkingRoot { get; init; } = Path.Combine(Path.GetTempPath(), "infra-agent-work");
}
