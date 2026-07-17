using LibGit2Sharp;
using Microsoft.Extensions.Options;

namespace InfraAgent.Tools.Git;

public sealed class LocalGitRepository(IOptions<GitOptions> options) : IGitRepository
{
    public Task<RepositoryPublishResult> PublishAsync(RepositoryPublishRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Directory.CreateDirectory(options.Value.LocalRepositoryRoot);
        var targetPath = Path.Combine(options.Value.LocalRepositoryRoot, request.RepositoryName);
        if (Directory.Exists(targetPath))
        {
            targetPath = $"{targetPath}-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";
        }

        CopyDirectory(request.WorkingDirectory, targetPath);

        Repository.Init(targetPath);
        using var repository = new Repository(targetPath);
        Commands.Stage(repository, "*");

        var signature = new Signature("InfraAgent", "infra-agent@example.local", DateTimeOffset.UtcNow);
        var commit = repository.Commit(request.CommitMessage, signature, signature);

        var files = Directory.EnumerateFiles(targetPath, "*", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}.git{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Select(path => Path.GetRelativePath(targetPath, path).Replace('\\', '/'))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return Task.FromResult(new RepositoryPublishResult(targetPath, commit.Sha, files));
    }

    private static void CopyDirectory(string sourceDirectory, string targetDirectory)
    {
        foreach (var directory in Directory.EnumerateDirectories(sourceDirectory, "*", SearchOption.AllDirectories)
                     .Where(path => !IsGeneratedToolDirectory(sourceDirectory, path)))
        {
            Directory.CreateDirectory(Path.Combine(targetDirectory, Path.GetRelativePath(sourceDirectory, directory)));
        }

        Directory.CreateDirectory(targetDirectory);
        foreach (var file in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories)
                     .Where(path => !IsGeneratedToolDirectory(sourceDirectory, path)))
        {
            var destination = Path.Combine(targetDirectory, Path.GetRelativePath(sourceDirectory, file));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination, overwrite: true);
        }
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
