using InfraAgent.Tools.Security;

namespace InfraAgent.Tests.Core;

public sealed class DeterministicSecurityPolicyTests
{
    private readonly DeterministicSecurityPolicy _policy = new();

    [Fact]
    public void FailsPublicS3Bucket()
    {
        var findings = _policy.EvaluateHcl("""
        resource "aws_s3_bucket" "bad" {
          bucket = "bad"
          acl    = "public-read"
        }
        """);

        Assert.Contains(findings, finding => finding.Code == "S3_PUBLIC");
        Assert.Contains(findings, finding => finding.Code == "S3_PUBLIC_BLOCK_MISSING");
    }

    [Fact]
    public void FailsUnencryptedS3Bucket()
    {
        var findings = _policy.EvaluateHcl("""
        resource "aws_s3_bucket" "bad" {
          bucket = "bad"
        }

        resource "aws_s3_bucket_public_access_block" "bad" {
          bucket                  = aws_s3_bucket.bad.id
          block_public_acls       = true
          block_public_policy     = true
          ignore_public_acls      = true
          restrict_public_buckets = true
        }
        """);

        Assert.Contains(findings, finding => finding.Code == "S3_UNENCRYPTED");
    }

    [Fact]
    public void FailsOpenSecurityGroupIngress()
    {
        var findings = _policy.EvaluateHcl("""
        resource "aws_security_group" "bad" {
          ingress {
            from_port   = 443
            to_port     = 443
            protocol    = "tcp"
            cidr_blocks = ["0.0.0.0/0"]
          }
        }
        """);

        Assert.Contains(findings, finding => finding.Code == "SG_OPEN_TO_WORLD");
    }

    [Fact]
    public void FailsWildcardIamPolicy()
    {
        var findings = _policy.EvaluateHcl("""
        resource "aws_iam_policy" "bad" {
          policy = jsonencode({
            Statement = [{
              Action = "*:*"
            }]
          })
        }
        """);

        Assert.Contains(findings, finding => finding.Code == "IAM_STAR_STAR");
    }
}
