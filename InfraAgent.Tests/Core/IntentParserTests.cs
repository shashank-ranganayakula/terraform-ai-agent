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
        var result = _parser.Parse("Create a public S3 bucket for static files in us-east-1");

        Assert.True(result.NeedsClarification);
        Assert.Contains("Public S3 buckets are disallowed", result.ClarifyingQuestion);
    }

    [Fact]
    public void IngressWithoutCidrRequiresClarification()
    {
        var result = _parser.Parse("Create a t3.medium EC2 instance in us-east-1 and allow SSH");

        Assert.True(result.NeedsClarification);
        Assert.Contains("CIDR", result.ClarifyingQuestion);
    }

    [Fact]
    public void OpenWorldIngressRequiresClarification()
    {
        var result = _parser.Parse("Create a t3.medium EC2 instance in us-east-1 and allow HTTP from 0.0.0.0/0");

        Assert.True(result.NeedsClarification);
        Assert.Contains("disallowed", result.ClarifyingQuestion);
    }

    [Fact]
    public void OpenIpv6IngressRequiresClarification()
    {
        var result = _parser.Parse("Create a t3.medium EC2 instance in us-east-1 and allow HTTPS from ::/0");

        Assert.True(result.NeedsClarification);
        Assert.Contains("disallowed", result.ClarifyingQuestion);
    }

    [Fact]
    public void InvalidIpv4CidrRequiresClarification()
    {
        var result = _parser.Parse("Create a t3.medium EC2 instance in us-east-1 and allow HTTP from 999.1.1.1/24");

        Assert.True(result.NeedsClarification);
        Assert.Contains("not a valid AWS CIDR", result.ClarifyingQuestion);
    }

    [Fact]
    public void DisallowedInstanceTypeRequiresClarification()
    {
        var result = _parser.Parse("Create an m7i.24xlarge EC2 instance in us-east-1");

        Assert.True(result.NeedsClarification);
        Assert.Contains("allowlist", result.ClarifyingQuestion);
    }

    [Fact]
    public void ExamplePromptProducesCompleteIntentWithPolicyAssumptions()
    {
        var result = _parser.Parse("Create an S3 bucket for user uploads with versioning enabled in us-east-1, and a t3.medium EC2 instance to run a web server");

        Assert.False(result.NeedsClarification);
        Assert.NotNull(result.Intent!.S3Bucket);
        Assert.NotNull(result.Intent.Ec2Instance);
        Assert.True(result.Intent.S3Bucket!.BlockPublicAccess);
        Assert.True(result.Intent.S3Bucket.ServerSideEncryptionEnabled);
        Assert.Equal("us-east-1", result.Intent.AwsRegion);
        Assert.Equal("t3.medium", result.Intent.Ec2Instance!.InstanceType);
        Assert.Empty(result.Intent.Ec2Instance.IngressRules);
    }

    [Fact]
    public void MissingRegionRequiresClarification()
    {
        var result = _parser.Parse("Create an encrypted S3 bucket for uploads");

        Assert.True(result.NeedsClarification);
        Assert.Contains("Which AWS region", result.ClarifyingQuestion);
    }

    [Fact]
    public void InvalidRegionRequiresClarification()
    {
        var result = _parser.Parse("Create an encrypted S3 bucket for uploads in us-east-99");

        Assert.True(result.NeedsClarification);
        Assert.Contains("not in the supported AWS region list", result.ClarifyingQuestion);
    }

    [Fact]
    public void CredentialLikePromptRequiresClarification()
    {
        var result = _parser.Parse("Create an S3 bucket in us-east-1 with aws_secret_access_key abc");

        Assert.True(result.NeedsClarification);
        Assert.Contains("Do not include cloud credentials", result.ClarifyingQuestion);
    }

    [Fact]
    public void DestructivePromptRequiresClarification()
    {
        var result = _parser.Parse("Delete an S3 bucket in us-east-1");

        Assert.True(result.NeedsClarification);
        Assert.Contains("Destructive operations are not supported", result.ClarifyingQuestion);
    }

    [Fact]
    public void UnsupportedProviderPromptRequiresClarification()
    {
        var result = _parser.Parse("Create an Azure storage account and S3 bucket in us-east-1");

        Assert.True(result.NeedsClarification);
        Assert.Contains("supports only AWS S3 buckets and EC2 instances", result.ClarifyingQuestion);
    }

    [Fact]
    public void VeryLongPromptRequiresClarification()
    {
        var parser = new IntentParser(Options.Create(new AgentOptions { MaxPromptCharacters = 20 }));

        var result = parser.Parse("Create an encrypted S3 bucket in us-east-1");

        Assert.True(result.NeedsClarification);
        Assert.Contains("20 characters or fewer", result.ClarifyingQuestion);
    }
}
