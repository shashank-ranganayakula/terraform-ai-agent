using System.Text;
using InfraAgent.Core.Context;
using InfraAgent.Core.Intent;

namespace InfraAgent.Core.Generation;

public sealed class TemplateTerraformGenerator : ITerraformGenerator
{
    public Task<GeneratedTerraform> GenerateAsync(
        InfrastructureIntent intent,
        IReadOnlyList<ContextDocument> context,
        string? repairInstructions,
        CancellationToken cancellationToken)
    {
        var main = new StringBuilder();
        main.AppendLine("terraform {");
        main.AppendLine("  required_version = \">= 1.5.0\"");
        main.AppendLine("  required_providers {");
        main.AppendLine("    aws = {");
        main.AppendLine("      source  = \"hashicorp/aws\"");
        main.AppendLine("      version = \"~> 5.0\"");
        main.AppendLine("    }");
        main.AppendLine("  }");
        main.AppendLine("}");
        main.AppendLine();
        main.AppendLine("provider \"aws\" {");
        main.AppendLine("  region = var.aws_region");
        main.AppendLine("}");
        main.AppendLine();

        if (intent.S3Bucket is not null)
        {
            AppendS3(main, intent.S3Bucket);
        }

        if (intent.Ec2Instance is not null)
        {
            AppendEc2(main, intent.Ec2Instance);
        }

        var variables = $$"""
        variable "aws_region" {
          description = "AWS region for this deployment."
          type        = string
          default     = "{{intent.AwsRegion}}"
        }

        variable "bucket_name" {
          description = "Globally unique S3 bucket name."
          type        = string
          default     = "infra-agent-uploads-example"
        }

        variable "instance_type" {
          description = "Allowed EC2 instance type selected during intent parsing."
          type        = string
          default     = "{{intent.Ec2Instance?.InstanceType ?? "t3.micro"}}"
        }
        """;

        var files = new Dictionary<string, string>
        {
            ["main.tf"] = main.ToString(),
            ["variables.tf"] = variables + Environment.NewLine
        };

        return Task.FromResult(new GeneratedTerraform(files, BuildSummary(intent), intent.Assumptions));
    }

    private static void AppendS3(StringBuilder main, S3BucketIntent bucket)
    {
        main.AppendLine("resource \"aws_s3_bucket\" \"uploads\" {");
        main.AppendLine("  bucket = var.bucket_name");
        main.AppendLine("}");
        main.AppendLine();
        main.AppendLine("resource \"aws_s3_bucket_versioning\" \"uploads\" {");
        main.AppendLine("  bucket = aws_s3_bucket.uploads.id");
        main.AppendLine();
        main.AppendLine("  versioning_configuration {");
        main.AppendLine($"    status = \"{(bucket.VersioningEnabled ? "Enabled" : "Suspended")}\"");
        main.AppendLine("  }");
        main.AppendLine("}");
        main.AppendLine();
        main.AppendLine("resource \"aws_s3_bucket_server_side_encryption_configuration\" \"uploads\" {");
        main.AppendLine("  bucket = aws_s3_bucket.uploads.id");
        main.AppendLine();
        main.AppendLine("  rule {");
        main.AppendLine("    apply_server_side_encryption_by_default {");
        main.AppendLine("      sse_algorithm = \"AES256\"");
        main.AppendLine("    }");
        main.AppendLine("  }");
        main.AppendLine("}");
        main.AppendLine();
        main.AppendLine("resource \"aws_s3_bucket_public_access_block\" \"uploads\" {");
        main.AppendLine("  bucket                  = aws_s3_bucket.uploads.id");
        main.AppendLine("  block_public_acls       = true");
        main.AppendLine("  block_public_policy     = true");
        main.AppendLine("  ignore_public_acls      = true");
        main.AppendLine("  restrict_public_buckets = true");
        main.AppendLine("}");
        main.AppendLine();
    }

    private static void AppendEc2(StringBuilder main, Ec2InstanceIntent instance)
    {
        main.AppendLine("data \"aws_ami\" \"amazon_linux\" {");
        main.AppendLine("  most_recent = true");
        main.AppendLine("  owners      = [\"amazon\"]");
        main.AppendLine();
        main.AppendLine("  filter {");
        main.AppendLine("    name   = \"name\"");
        main.AppendLine($"    values = [\"{instance.AmiNamePattern}\"]");
        main.AppendLine("  }");
        main.AppendLine();
        main.AppendLine("  filter {");
        main.AppendLine("    name   = \"virtualization-type\"");
        main.AppendLine("    values = [\"hvm\"]");
        main.AppendLine("  }");
        main.AppendLine("}");
        main.AppendLine();
        main.AppendLine("resource \"aws_security_group\" \"web\" {");
        main.AppendLine("  name        = \"infra-agent-web\"");
        main.AppendLine("  description = \"Security group managed by InfraAgent Phase 1\"");
        main.AppendLine();
        foreach (var rule in instance.IngressRules)
        {
            main.AppendLine("  ingress {");
            main.AppendLine($"    description = \"{rule.Description}\"");
            main.AppendLine($"    from_port   = {rule.FromPort}");
            main.AppendLine($"    to_port     = {rule.ToPort}");
            main.AppendLine($"    protocol    = \"{rule.Protocol}\"");
            main.AppendLine($"    cidr_blocks = [\"{rule.CidrBlock}\"]");
            main.AppendLine("  }");
            main.AppendLine();
        }

        main.AppendLine("  egress {");
        main.AppendLine("    from_port   = 0");
        main.AppendLine("    to_port     = 0");
        main.AppendLine("    protocol    = \"-1\"");
        main.AppendLine("    cidr_blocks = [\"0.0.0.0/0\"]");
        main.AppendLine("  }");
        main.AppendLine("}");
        main.AppendLine();
        main.AppendLine("resource \"aws_instance\" \"web\" {");
        main.AppendLine("  ami                    = data.aws_ami.amazon_linux.id");
        main.AppendLine("  instance_type          = var.instance_type");
        main.AppendLine("  vpc_security_group_ids = [aws_security_group.web.id]");
        main.AppendLine();
        main.AppendLine("  tags = {");
        main.AppendLine("    Name = \"infra-agent-web\"");
        main.AppendLine("  }");
        main.AppendLine("}");
        main.AppendLine();
    }

    private static string BuildSummary(InfrastructureIntent intent)
    {
        var resources = new List<string>();
        if (intent.S3Bucket is not null)
        {
            resources.Add("an encrypted S3 bucket with public access blocked");
        }

        if (intent.Ec2Instance is not null)
        {
            resources.Add($"a {intent.Ec2Instance.InstanceType} EC2 instance with an associated security group and Amazon Linux AMI lookup");
        }

        return $"Generated Terraform for {string.Join(" and ", resources)}.";
    }
}
