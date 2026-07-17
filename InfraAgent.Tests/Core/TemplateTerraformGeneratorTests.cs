using InfraAgent.Core.Generation;
using InfraAgent.Core.Intent;

namespace InfraAgent.Tests.Core;

public sealed class TemplateTerraformGeneratorTests
{
    [Fact]
    public async Task GeneratedHclContainsExpectedPhaseOneResources()
    {
        var intent = new InfrastructureIntent(
            "Create S3 and EC2",
            "us-east-1",
            new S3BucketIntent("uploads", VersioningEnabled: true, BlockPublicAccess: true, ServerSideEncryptionEnabled: true),
            new Ec2InstanceIntent(
                "web",
                "t3.medium",
                "al2023-ami-2023.*-x86_64",
                [new IngressRuleIntent(80, 80, "tcp", "10.0.0.0/8", "HTTP")]),
            ["test assumption"]);

        var result = await new TemplateTerraformGenerator().GenerateAsync(intent, [], null, CancellationToken.None);
        var hcl = string.Join(Environment.NewLine, result.Files.Values);

        Assert.Contains("resource \"aws_s3_bucket\"", hcl);
        Assert.Contains("resource \"aws_s3_bucket_versioning\"", hcl);
        Assert.Contains("resource \"aws_s3_bucket_server_side_encryption_configuration\"", hcl);
        Assert.Contains("resource \"aws_s3_bucket_public_access_block\"", hcl);
        Assert.Contains("data \"aws_ami\"", hcl);
        Assert.Contains("resource \"aws_security_group\"", hcl);
        Assert.Contains("resource \"aws_instance\"", hcl);
        Assert.Contains("cidr_blocks = [\"10.0.0.0/8\"]", hcl);
    }
}
