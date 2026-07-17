namespace InfraAgent.Tools.Git;

public sealed record RepositoryPublishRequest(
    string WorkingDirectory,
    string RepositoryName,
    string CommitMessage,
    string Summary);
