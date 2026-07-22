using InfraAgent.Core.Context;
using InfraAgent.Core.Generation;
using InfraAgent.Core.Intent;
using InfraAgent.Core.Options;
using InfraAgent.Core.Orchestration;
using InfraAgent.Core.Provisioning;
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
        var provisioner = new FakeInfrastructureProvisioner(ProvisioningResult.Success("apply complete"));
        var git = new FakeGitRepository();

        var agent = new InfrastructureAgent(
            new IntentParser(Options.Create(new AgentOptions { AllowedEc2InstanceTypes = ["t3.medium"] })),
            new EmptyContextRetriever(),
            generator,
            validator,
            provisioner,
            git,
            options,
            NullLogger<InfrastructureAgent>.Instance);

        var response = await agent.GenerateAsync("Create a t3.medium EC2 instance", CancellationToken.None);

        Assert.Equal("succeeded", response.Status);
        Assert.Equal(2, generator.Calls);
        Assert.Equal(2, validator.Calls);
        Assert.Equal(1, provisioner.Calls);
        Assert.NotNull(git.LastRequest);
        Assert.Contains("README.md", response.FilesCreated);
        Assert.Equal("applied", response.ProvisioningStatus);
    }

    [Fact]
    public async Task PublishesRepositoryBeforeTerraformApply()
    {
        var workingRoot = Path.Combine(Path.GetTempPath(), $"infra-agent-tests-{Guid.NewGuid():N}");
        var options = Options.Create(new AgentOptions
        {
            WorkingRoot = workingRoot,
            MaxRepairAttempts = 1,
            AllowedEc2InstanceTypes = ["t3.medium"]
        });

        var generator = new SequenceTerraformGenerator(
            new GeneratedTerraform(new Dictionary<string, string> { ["main.tf"] = "resource \"aws_s3_bucket\" \"ok\" {}" }, "ok", []));
        var validator = new SequenceValidator(ValidationResult.Success("ok"));
        var provisioner = new FakeInfrastructureProvisioner(ProvisioningResult.Failure("apply failed"));
        var git = new FakeGitRepository();

        var agent = new InfrastructureAgent(
            new IntentParser(Options.Create(new AgentOptions { AllowedEc2InstanceTypes = ["t3.medium"] })),
            new EmptyContextRetriever(),
            generator,
            validator,
            provisioner,
            git,
            options,
            NullLogger<InfrastructureAgent>.Instance);

        var response = await agent.GenerateAsync("Create a t3.medium EC2 instance", CancellationToken.None);

        Assert.Equal("failed", response.Status);
        Assert.Equal("failed", response.ProvisioningStatus);
        Assert.Contains("apply failed", response.ProvisioningOutput);
        Assert.NotNull(git.LastRequest);
        Assert.Equal("https://example.test/repo", response.RepositoryUrl);
    }

    private sealed class EmptyContextRetriever : IContextRetriever
    {
        public Task<IReadOnlyList<ContextDocument>> RetrieveAsync(InfrastructureIntent intent, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ContextDocument>>(Array.Empty<ContextDocument>());
    }
}
