using System.Text;
using System.Text.RegularExpressions;

namespace InfraAgent.Core.Generation;

public static class TerraformSecurityDefaults
{
    private static readonly Regex S3BucketResourcePattern = new(
        @"resource\s+""aws_s3_bucket""\s+""(?<name>[A-Za-z0-9_]+)""",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex SseAlgorithmPattern = new(
        @"sse_algorithm\s*=\s*(?<value>[^\r\n]+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static GeneratedTerraform EnsureS3Defaults(GeneratedTerraform terraform)
    {
        var files = terraform.Files.ToDictionary(
            file => file.Key,
            file => file.Value,
            StringComparer.OrdinalIgnoreCase);

        var hcl = string.Join(Environment.NewLine, files.Where(file => IsTerraformFile(file.Key)).Select(file => file.Value));
        var bucketNames = S3BucketResourcePattern
            .Matches(hcl)
            .Select(match => match.Groups["name"].Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (bucketNames.Length == 0)
        {
            return terraform;
        }

        NormalizeExistingEncryptionAlgorithms(files);
        NormalizeExistingPublicAccessBlockFlags(files);

        hcl = string.Join(Environment.NewLine, files.Where(file => IsTerraformFile(file.Key)).Select(file => file.Value));
        var additions = new StringBuilder();

        foreach (var bucketName in bucketNames)
        {
            if (!HasS3CompanionResource(hcl, "aws_s3_bucket_server_side_encryption_configuration", bucketName))
            {
                additions.AppendLine(BuildEncryptionResource(bucketName));
            }

            if (!HasS3CompanionResource(hcl, "aws_s3_bucket_public_access_block", bucketName))
            {
                additions.AppendLine(BuildPublicAccessBlockResource(bucketName));
            }
        }

        if (additions.Length == 0)
        {
            return new GeneratedTerraform(files, terraform.Summary, terraform.Assumptions);
        }

        files["security-defaults.tf"] = additions.ToString().TrimEnd() + Environment.NewLine;
        var assumptions = terraform.Assumptions
            .Concat(["Applied deterministic S3 encryption and public access block defaults."])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new GeneratedTerraform(files, terraform.Summary, assumptions);
    }

    private static bool IsTerraformFile(string path) =>
        path.EndsWith(".tf", StringComparison.OrdinalIgnoreCase);

    private static bool HasS3CompanionResource(string hcl, string resourceType, string bucketName) =>
        HasResourceNamed(hcl, resourceType, bucketName) || ReferencesBucketResource(hcl, resourceType, bucketName);

    private static bool HasResourceNamed(string hcl, string resourceType, string resourceName) =>
        Regex.IsMatch(
            hcl,
            $@"resource\s+""{Regex.Escape(resourceType)}""\s+""{Regex.Escape(resourceName)}""",
            RegexOptions.IgnoreCase);

    private static bool ReferencesBucketResource(string hcl, string resourceType, string bucketName) =>
        Regex.IsMatch(
            hcl,
            $@"resource\s+""{Regex.Escape(resourceType)}""\s+""[^""]+""\s*\{{(?:(?!^\}}).)*bucket\s*=\s*aws_s3_bucket\.{Regex.Escape(bucketName)}\.(id|bucket)(?:(?!^\}}).)*^\}}",
            RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Singleline);

    private static void NormalizeExistingEncryptionAlgorithms(IDictionary<string, string> files)
    {
        foreach (var file in files.Keys.Where(IsTerraformFile).ToArray())
        {
            files[file] = SseAlgorithmPattern.Replace(files[file], @"sse_algorithm = ""AES256""");
        }
    }

    private static void NormalizeExistingPublicAccessBlockFlags(IDictionary<string, string> files)
    {
        foreach (var file in files.Keys.Where(IsTerraformFile).ToArray())
        {
            var content = files[file];
            content = Regex.Replace(content, @"block_public_acls\s*=\s*false", "block_public_acls       = true", RegexOptions.IgnoreCase);
            content = Regex.Replace(content, @"block_public_policy\s*=\s*false", "block_public_policy     = true", RegexOptions.IgnoreCase);
            content = Regex.Replace(content, @"ignore_public_acls\s*=\s*false", "ignore_public_acls      = true", RegexOptions.IgnoreCase);
            content = Regex.Replace(content, @"restrict_public_buckets\s*=\s*false", "restrict_public_buckets = true", RegexOptions.IgnoreCase);
            files[file] = content;
        }
    }

    private static string BuildEncryptionResource(string bucketName) =>
        $$"""
        resource "aws_s3_bucket_server_side_encryption_configuration" "{{bucketName}}" {
          bucket = aws_s3_bucket.{{bucketName}}.id

          rule {
            apply_server_side_encryption_by_default {
              sse_algorithm = "AES256"
            }
          }
        }

        """;

    private static string BuildPublicAccessBlockResource(string bucketName) =>
        $$"""
        resource "aws_s3_bucket_public_access_block" "{{bucketName}}" {
          bucket                  = aws_s3_bucket.{{bucketName}}.id
          block_public_acls       = true
          block_public_policy     = true
          ignore_public_acls      = true
          restrict_public_buckets = true
        }

        """;
}
