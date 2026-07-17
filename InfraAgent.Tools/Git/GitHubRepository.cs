using LibGit2Sharp;
using Microsoft.Extensions.Options;
using Octokit;

namespace InfraAgent.Tools.Git;

public sealed class GitHubRepository(IOptions<GitOptions> options) : IGitRepository
{
    public async Task<RepositoryPublishResult> PublishAsync(RepositoryPublishRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(options.Value.GitHubToken))
        {
            throw new InvalidOperationException("GitHub token is required for remote publishing.");
        }

        var client = new GitHubClient(new ProductHeaderValue("InfraAgent"))
        {
            Credentials = new Octokit.Credentials(options.Value.GitHubToken)
        };

        var newRepository = new NewRepository(request.RepositoryName)
        {
            Private = options.Value.UsePrivateRepositories,
            AutoInit = false
        };

        Octokit.Repository remoteRepository = string.IsNullOrWhiteSpace(options.Value.GitHubOwner)
            ? await client.Repository.Create(newRepository)
            : await client.Repository.Create(options.Value.GitHubOwner, newRepository);

        LibGit2Sharp.Repository.Init(request.WorkingDirectory);
        using var repository = new LibGit2Sharp.Repository(request.WorkingDirectory);
        Commands.Stage(repository, "*");

        var signature = new LibGit2Sharp.Signature("InfraAgent", "infra-agent@example.local", DateTimeOffset.UtcNow);
        var commit = repository.Commit(request.CommitMessage, signature, signature);
        var remote = repository.Network.Remotes.Add("origin", remoteRepository.CloneUrl);

        var pushOptions = new PushOptions
        {
            CredentialsProvider = (_, _, _) => new UsernamePasswordCredentials
            {
                Username = "x-access-token",
                Password = options.Value.GitHubToken
            }
        };

        repository.Network.Push(remote, "refs/heads/master:refs/heads/main", pushOptions);

        var files = Directory.EnumerateFiles(request.WorkingDirectory, "*", SearchOption.AllDirectories)
            .Where(path => !IsGeneratedToolDirectory(request.WorkingDirectory, path))
            .Select(path => Path.GetRelativePath(request.WorkingDirectory, path).Replace('\\', '/'))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new RepositoryPublishResult(remoteRepository.HtmlUrl, commit.Sha, files);
    }

    private static bool IsGeneratedToolDirectory(string sourceDirectory, string path)
    {
        var relative = Path.GetRelativePath(sourceDirectory, path).Replace('\\', '/');
        return relative == ".terraform" ||
            relative.StartsWith(".terraform/", StringComparison.OrdinalIgnoreCase) ||
            relative == ".git" ||
            relative.StartsWith(".git/", StringComparison.OrdinalIgnoreCase);
    }
}
