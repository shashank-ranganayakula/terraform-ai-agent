using InfraAgent.Core.Context;
using InfraAgent.Core.Generation;
using InfraAgent.Core.Intent;
using InfraAgent.Core.Options;
using InfraAgent.Core.Orchestration;
using InfraAgent.Core.Validation;
using InfraAgent.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace InfraAgent.Tests.Core;

public sealed class InfrastructureAgentTests
{
    [Fact]
    public async Task RetriesGenerationUntilValidationSucceeds()
    {
        var workingRoot = Path.Combine(Path.GetTempPath(), $"infra-agent-tests-{Guid.NewGuid():N}");
        var options = Options.Create(new AgentOptions
        {
            WorkingRoot = workingRoot,
            MaxRepairAttempts = 3,
            AllowedEc2InstanceTypes = ["t3.medium"]
        });

        var generator = new SequenceTerraformGenerator(
            new GeneratedTerraform(new Dictionary<string, string> { ["main.tf"] = "bad" }, "bad", []),
            new GeneratedTerraform(new Dictionary<string, string> { ["main.tf"] = "resource \"aws_s3_bucket\" \"ok\" {}" }, "ok", ["fixed"]));
        var validator = new SequenceValidator(
            ValidationResult.Failure("terraform validate failed"),
            ValidationResult.Success("ok"));
        var git = new FakeGitRepository();

        var agent = new InfrastructureAgent(
            new IntentParser(Options.Create(new AgentOptions { AllowedEc2InstanceTypes = ["t3.medium"] })),
            new EmptyContextRetriever(),
            generator,
            validator,
            git,
            options,
            NullLogger<InfrastructureAgent>.Instance);

        var response = await agent.GenerateAsync("Create a t3.medium EC2 instance", CancellationToken.None);

        Assert.Equal("succeeded", response.Status);
        Assert.Equal(2, generator.Calls);
        Assert.Equal(2, validator.Calls);
        Assert.NotNull(git.LastRequest);
        Assert.Contains("README.md", response.FilesCreated);
    }

    private sealed class EmptyContextRetriever : IContextRetriever
    {
        public Task<IReadOnlyList<ContextDocument>> RetrieveAsync(InfrastructureIntent intent, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ContextDocument>>(Array.Empty<ContextDocument>());
    }
}
