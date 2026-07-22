namespace InfraAgent.Tools.Git;

public interface IGitRepository
{
    Task<RepositoryPublishResult> PublishAsync(RepositoryPublishRequest request, CancellationToken cancellationToken);
}
