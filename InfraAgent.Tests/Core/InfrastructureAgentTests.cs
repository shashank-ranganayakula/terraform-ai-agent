using InfraAgent.Core.Context;
using InfraAgent.Core.Generation;
using InfraAgent.Core.Intent;
using InfraAgent.Core.Options;
using InfraAgent.Core.Orchestration;
using InfraAgent.Core.Preflight;
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
            new FakeS3BucketAvailabilityChecker(),
            git,
            options,
            NullLogger<InfrastructureAgent>.Instance);

        var response = await agent.GenerateAsync("Create a t3.medium EC2 instance in us-east-1", CancellationToken.None);

        Assert.Equal("succeeded", response.Status);
        Assert.Equal(2, generator.Calls);
        Assert.Equal(2, validator.Calls);
        Assert.Single(validator.WorkingDirectories.Distinct(StringComparer.OrdinalIgnoreCase));
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
            new FakeS3BucketAvailabilityChecker(),
            git,
            options,
            NullLogger<InfrastructureAgent>.Instance);

        var response = await agent.GenerateAsync("Create a t3.medium EC2 instance in us-east-1", CancellationToken.None);

        Assert.Equal("failed", response.Status);
        Assert.Equal("failed", response.ProvisioningStatus);
        Assert.Contains("apply failed", response.ProvisioningOutput);
        Assert.NotNull(git.LastRequest);
        Assert.Equal("https://example.test/repo", response.RepositoryUrl);
    }

    [Fact]
    public async Task StopsBeforeGenerationWhenRequestedS3BucketAlreadyExists()
    {
        var workingRoot = Path.Combine(Path.GetTempPath(), $"infra-agent-tests-{Guid.NewGuid():N}");
        var options = Options.Create(new AgentOptions
        {
            WorkingRoot = workingRoot,
            MaxRepairAttempts = 3
        });

        var generator = new SequenceTerraformGenerator(
            new GeneratedTerraform(new Dictionary<string, string> { ["main.tf"] = "resource \"aws_s3_bucket\" \"ok\" {}" }, "ok", []));
        var validator = new SequenceValidator(ValidationResult.Success("ok"));
        var provisioner = new FakeInfrastructureProvisioner(ProvisioningResult.Success("apply complete"));
        var git = new FakeGitRepository();
        var bucketCheck = new FakeS3BucketAvailabilityChecker(
            S3BucketAvailabilityResult.Exists("taken-bucket", "ap-south-2"));

        var agent = new InfrastructureAgent(
            new IntentParser(Options.Create(new AgentOptions())),
            new EmptyContextRetriever(),
            generator,
            validator,
            provisioner,
            bucketCheck,
            git,
            options,
            NullLogger<InfrastructureAgent>.Instance);

        var response = await agent.GenerateAsync(
            "Create an encrypted S3 bucket named taken-bucket in ap-south-2",
            CancellationToken.None);

        Assert.Equal("failed", response.Status);
        Assert.Equal("not_started", response.ProvisioningStatus);
        Assert.Contains("already exists", response.Error);
        Assert.Contains("taken-bucket", response.ProvisioningOutput);
        Assert.Equal(1, bucketCheck.Calls);
        Assert.Equal(0, generator.Calls);
        Assert.Equal(0, validator.Calls);
        Assert.Equal(0, provisioner.Calls);
        Assert.Null(git.LastRequest);
    }

    private sealed class EmptyContextRetriever : IContextRetriever
    {
        public Task<IReadOnlyList<ContextDocument>> RetrieveAsync(InfrastructureIntent intent, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ContextDocument>>(Array.Empty<ContextDocument>());
    }
}
