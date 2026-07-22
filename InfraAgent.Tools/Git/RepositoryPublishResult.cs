namespace InfraAgent.Tools.Git;

public sealed record RepositoryPublishResult(string RepositoryUrl, string CommitSha, IReadOnlyList<string> Files);
