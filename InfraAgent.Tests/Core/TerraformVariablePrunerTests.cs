using InfraAgent.Core.Generation;

namespace InfraAgent.Tests.Core;

public sealed class TerraformVariablePrunerTests
{
    [Fact]
    public void RemovesUnusedVariableDeclarationAndTfvarsAssignment()
    {
        var terraform = new GeneratedTerraform(
            new Dictionary<string, string>
            {
                ["main.tf"] = """
                resource "aws_s3_bucket" "uploads" {
                  bucket = var.bucket_name
                }
                """,
                ["variables.tf"] = """
                variable "bucket_name" {
                  type = string
                }

                variable "server_side_encryption_enabled" {
                  type    = bool
                  default = true
                }
                """,
                ["terraform.tfvars"] = """
                bucket_name                    = "uploads"
                server_side_encryption_enabled = true
                """
            },
            "summary",
            []);

        var pruned = TerraformVariablePruner.PruneUnusedVariables(terraform);

        Assert.Contains("variable \"bucket_name\"", pruned.Files["variables.tf"]);
        Assert.DoesNotContain("server_side_encryption_enabled", pruned.Files["variables.tf"]);
        Assert.DoesNotContain("server_side_encryption_enabled", pruned.Files["terraform.tfvars"]);
        Assert.Contains(pruned.Assumptions, assumption => assumption.Contains("Removed unused Terraform variables", StringComparison.Ordinal));
    }

    [Fact]
    public void RemovesUndeclaredTfvarsAssignments()
    {
        var terraform = new GeneratedTerraform(
            new Dictionary<string, string>
            {
                ["main.tf"] = """
                resource "aws_s3_bucket" "uploads" {
                  bucket = var.bucket_name
                }
                """,
                ["variables.tf"] = """
                variable "bucket_name" {
                  type = string
                }
                """,
                ["terraform.tfvars"] = """
                bucket_name = "uploads"
                tags = {
                  Environment = "dev"
                }
                """
            },
            "summary",
            []);

        var pruned = TerraformVariablePruner.PruneUnusedVariables(terraform);

        Assert.Contains("bucket_name = \"uploads\"", pruned.Files["terraform.tfvars"]);
        Assert.DoesNotContain("tags", pruned.Files["terraform.tfvars"]);
        Assert.DoesNotContain("Environment", pruned.Files["terraform.tfvars"]);
        Assert.Contains(pruned.Assumptions, assumption => assumption.Contains("Removed undeclared terraform.tfvars assignments", StringComparison.Ordinal));
    }
}
