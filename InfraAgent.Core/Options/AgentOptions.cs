namespace InfraAgent.Core.Options;

public sealed class AgentOptions
{
    public string DefaultAwsRegion { get; init; } = "us-east-1";

    public string[] AllowedEc2InstanceTypes { get; init; } = ["t3.micro", "t3.small", "t3.medium"];

    public int MaxRepairAttempts { get; init; } = 3;

    public string WorkingRoot { get; init; } = Path.Combine(Path.GetTempPath(), "infra-agent-work");
}
