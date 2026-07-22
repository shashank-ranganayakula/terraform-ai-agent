using InfraAgent.Core.Generation;
using InfraAgent.Core.Intent;
using InfraAgent.Tools.Processes;
using InfraAgent.Tools.Terraform;
using Microsoft.Extensions.Logging.Abstractions;

namespace InfraAgent.Tests.Integration;

public sealed class TerraformRunnerIntegrationTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task TerraformValidateAcceptsGeneratedTemplateWhenCliIsAvailable()
    {
        if (!IsOnPath("terraform"))
        {
            return;
        }

        var workingDirectory = Path.Combine(Path.GetTempPath(), $"infra-agent-tf-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workingDirectory);

        var intent = new InfrastructureIntent(
            "Create S3",
            "us-east-1",
            new S3BucketIntent("uploads", true, true, true),
            null,
            []);

        var generated = await new TemplateTerraformGenerator().GenerateAsync(intent, [], null, CancellationToken.None);
        foreach (var file in generated.Files)
        {
            await File.WriteAllTextAsync(Path.Combine(workingDirectory, file.Key), file.Value);
        }

        var runner = new TerraformRunner(new ProcessRunner(NullLogger<ProcessRunner>.Instance));
        var init = await runner.InitAsync(workingDirectory, CancellationToken.None);
        var validate = await runner.ValidateAsync(workingDirectory, CancellationToken.None);

        Assert.True(init.Succeeded, init.CombinedOutput);
        Assert.True(validate.Succeeded, validate.CombinedOutput);
    }

    private static bool IsOnPath(string executable)
    {
        var paths = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator);
        var extensions = OperatingSystem.IsWindows()
            ? (Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE").Split(';')
            : [string.Empty];

        return paths.Any(path => extensions.Any(extension => File.Exists(Path.Combine(path, executable + extension))));
    }
}
