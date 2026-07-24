using InfraAgent.Core.Generation;

namespace InfraAgent.Tests.Core;

public sealed class TerraformSecurityDefaultsTests
{
    [Fact]
    public void AddsMissingS3EncryptionAndPublicAccessBlock()
    {
        var terraform = new GeneratedTerraform(
            new Dictionary<string, string>
            {
                ["main.tf"] = """
                resource "aws_s3_bucket" "uploads" {
                  bucket = "uploads"
                }
                """
            },
            "summary",
            []);

        var hardened = TerraformSecurityDefaults.EnsureS3Defaults(terraform);

        Assert.True(hardened.Files.TryGetValue("security-defaults.tf", out var defaults));
        Assert.Contains("resource \"aws_s3_bucket_server_side_encryption_configuration\" \"uploads\"", defaults);
        Assert.Contains("resource \"aws_s3_bucket_public_access_block\" \"uploads\"", defaults);
        Assert.Contains("sse_algorithm = \"AES256\"", defaults);
    }

    [Fact]
    public void NormalizesVariableDrivenEncryptionAlgorithm()
    {
        var terraform = new GeneratedTerraform(
            new Dictionary<string, string>
            {
                ["main.tf"] = """
                resource "aws_s3_bucket" "uploads" {
                  bucket = "uploads"
                }

                resource "aws_s3_bucket_server_side_encryption_configuration" "uploads" {
                  bucket = aws_s3_bucket.uploads.id

                  rule {
                    apply_server_side_encryption_by_default {
                      sse_algorithm = var.encryption_algorithm
                    }
                  }
                }
                """
            },
            "summary",
            []);

        var hardened = TerraformSecurityDefaults.EnsureS3Defaults(terraform);

        Assert.Contains("sse_algorithm = \"AES256\"", hardened.Files["main.tf"]);
        Assert.DoesNotContain("sse_algorithm = var.encryption_algorithm", hardened.Files["main.tf"]);
    }

    [Fact]
    public void SecurityDefaultsThenPrunerRemovesNowUnusedEncryptionVariable()
    {
        var terraform = new GeneratedTerraform(
            new Dictionary<string, string>
            {
                ["main.tf"] = """
                resource "aws_s3_bucket" "uploads" {
                  bucket = "uploads"
                }

                resource "aws_s3_bucket_server_side_encryption_configuration" "uploads" {
                  bucket = aws_s3_bucket.uploads.id

                  rule {
                    apply_server_side_encryption_by_default {
                      sse_algorithm = var.encryption_algorithm
                    }
                  }
                }
                """,
                ["variables.tf"] = """
                variable "encryption_algorithm" {
                  type    = string
                  default = "AES256"
                }
                """,
                ["terraform.tfvars"] = """
                encryption_algorithm = "AES256"
                """
            },
            "summary",
            []);

        var hardened = TerraformVariablePruner.PruneUnusedVariables(TerraformSecurityDefaults.EnsureS3Defaults(terraform));

        Assert.DoesNotContain("variable \"encryption_algorithm\"", hardened.Files["variables.tf"]);
        Assert.DoesNotContain("encryption_algorithm", hardened.Files["terraform.tfvars"]);
        Assert.Contains("sse_algorithm = \"AES256\"", hardened.Files["main.tf"]);
    }

    [Fact]
    public void DoesNotAddDuplicateCompanionResourceWhenSameNameExists()
    {
        var terraform = new GeneratedTerraform(
            new Dictionary<string, string>
            {
                ["main.tf"] = """
                resource "aws_s3_bucket" "uploads" {
                  bucket = "uploads"
                }

                resource "aws_s3_bucket_server_side_encryption_configuration" "uploads" {
                  bucket = var.bucket_id

                  rule {
                    apply_server_side_encryption_by_default {
                      sse_algorithm = var.encryption_algorithm
                    }
                  }
                }
                """
            },
            "summary",
            []);

        var hardened = TerraformSecurityDefaults.EnsureS3Defaults(terraform);
        var hcl = string.Join(Environment.NewLine, hardened.Files.Values);

        Assert.Equal(1, CountOccurrences(hcl, "resource \"aws_s3_bucket_server_side_encryption_configuration\" \"uploads\""));
        Assert.Contains("sse_algorithm = \"AES256\"", hcl);
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }
}
