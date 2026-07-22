using InfraAgent.Tools.Git;

namespace InfraAgent.Tests.Fakes;

public sealed class FakeGitRepository : IGitRepository
{
    public RepositoryPublishRequest? LastRequest { get; private set; }

    public Task<RepositoryPublishResult> PublishAsync(RepositoryPublishRequest request, CancellationToken cancellationToken)
    {
        LastRequest = request;
        var files = Directory.EnumerateFiles(request.WorkingDirectory, "*", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}.git{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Select(path => Path.GetRelativePath(request.WorkingDirectory, path).Replace('\\', '/'))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return Task.FromResult(new RepositoryPublishResult("https://example.test/repo", "abc123", files));
    }
}
