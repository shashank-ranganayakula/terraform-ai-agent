namespace InfraAgent.Tools.Git;

public sealed class GitOptions
{
    public string? GitHubToken { get; init; }

    public string? GitHubOwner { get; init; }

    public bool UsePrivateRepositories { get; init; } = true;

    public string LocalRepositoryRoot { get; init; } = Path.Combine(Path.GetTempPath(), "infra-agent-repos");
}
