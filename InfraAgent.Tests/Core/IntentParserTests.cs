using InfraAgent.Core.Intent;
using InfraAgent.Core.Options;
using Microsoft.Extensions.Options;

namespace InfraAgent.Tests.Core;

public sealed class IntentParserTests
{
    private readonly IntentParser _parser = new(Options.Create(new AgentOptions
    {
        AllowedEc2InstanceTypes = ["t3.micro", "t3.small", "t3.medium"]
    }));

    [Fact]
    public void PublicS3RequestRequiresClarification()
    {
        var result = _parser.Parse("Create a public S3 bucket for static files");

        Assert.True(result.NeedsClarification);
        Assert.Contains("Public S3 buckets are disallowed", result.ClarifyingQuestion);
    }

    [Fact]
    public void IngressWithoutCidrRequiresClarification()
    {
        var result = _parser.Parse("Create a t3.medium EC2 instance and allow SSH");

        Assert.True(result.NeedsClarification);
        Assert.Contains("CIDR", result.ClarifyingQuestion);
    }

    [Fact]
    public void OpenWorldIngressRequiresClarification()
    {
        var result = _parser.Parse("Create a t3.medium EC2 instance and allow HTTP from 0.0.0.0/0");

        Assert.True(result.NeedsClarification);
        Assert.Contains("disallowed", result.ClarifyingQuestion);
    }

    [Fact]
    public void DisallowedInstanceTypeRequiresClarification()
    {
        var result = _parser.Parse("Create an m7i.24xlarge EC2 instance");

        Assert.True(result.NeedsClarification);
        Assert.Contains("allowlist", result.ClarifyingQuestion);
    }

    [Fact]
    public void ExamplePromptProducesCompleteIntentWithPolicyAssumptions()
    {
        var result = _parser.Parse("Create an S3 bucket for user uploads with versioning enabled, and a t3.medium EC2 instance to run a web server");

        Assert.False(result.NeedsClarification);
        Assert.NotNull(result.Intent!.S3Bucket);
        Assert.NotNull(result.Intent.Ec2Instance);
        Assert.True(result.Intent.S3Bucket!.BlockPublicAccess);
        Assert.True(result.Intent.S3Bucket.ServerSideEncryptionEnabled);
        Assert.Equal("t3.medium", result.Intent.Ec2Instance!.InstanceType);
        Assert.Empty(result.Intent.Ec2Instance.IngressRules);
    }
}
