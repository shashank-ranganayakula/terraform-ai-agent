using System.Text.RegularExpressions;

namespace InfraAgent.Tools.Security;

public sealed class DeterministicSecurityPolicy
{
    private static readonly Regex S3BucketResource = new(
        @"resource\s+""aws_s3_bucket""\s+""[^""]+""",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex SecurityGroupBlock = new(
        @"resource\s+""aws_security_group""\s+""[^""]+""\s*\{(?<body>.*?)^\}",
        RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex PublicCidr = new(
        @"(cidr_blocks|ipv6_cidr_blocks)\s*=\s*\[[^\]]*(""(0\.0\.0\.0/0|::/0)"")",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public IReadOnlyList<SecurityFinding> EvaluateDirectory(string workingDirectory)
    {
        var hcl = string.Join(
            Environment.NewLine,
            Directory.EnumerateFiles(workingDirectory, "*.tf", SearchOption.TopDirectoryOnly)
                .Select(File.ReadAllText));

        return EvaluateHcl(hcl);
    }

    public IReadOnlyList<SecurityFinding> EvaluateHcl(string hcl)
    {
        var findings = new List<SecurityFinding>();

        if (Regex.IsMatch(hcl, @"acl\s*=\s*""public-", RegexOptions.IgnoreCase) ||
            Regex.IsMatch(hcl, @"policy\s*=.*Principal\s*:\s*""\*""", RegexOptions.IgnoreCase | RegexOptions.Singleline))
        {
            findings.Add(new SecurityFinding("S3_PUBLIC", "S3 bucket policy or ACL appears to allow public access.", "HIGH"));
        }

        if (S3BucketResource.IsMatch(hcl))
        {
            if (!Regex.IsMatch(hcl, @"resource\s+""aws_s3_bucket_public_access_block""", RegexOptions.IgnoreCase) ||
                Regex.IsMatch(hcl, @"(block_public_acls|block_public_policy|ignore_public_acls|restrict_public_buckets)\s*=\s*false", RegexOptions.IgnoreCase))
            {
                findings.Add(new SecurityFinding("S3_PUBLIC_BLOCK_MISSING", "S3 buckets must include a public access block with every flag set to true.", "HIGH"));
            }

            if (!Regex.IsMatch(hcl, @"resource\s+""aws_s3_bucket_server_side_encryption_configuration""", RegexOptions.IgnoreCase) ||
                !Regex.IsMatch(hcl, @"sse_algorithm\s*=\s*""(AES256|aws:kms)""", RegexOptions.IgnoreCase))
            {
                findings.Add(new SecurityFinding("S3_UNENCRYPTED", "S3 buckets must configure server-side encryption.", "HIGH"));
            }
        }

        foreach (Match securityGroup in SecurityGroupBlock.Matches(hcl))
        {
            var body = securityGroup.Groups["body"].Value;
            if (Regex.IsMatch(body, @"ingress\s*\{", RegexOptions.IgnoreCase) && PublicCidr.IsMatch(body))
            {
                findings.Add(new SecurityFinding("SG_OPEN_TO_WORLD", "Security group ingress must not allow 0.0.0.0/0 or ::/0 on any port.", "CRITICAL"));
            }
        }

        if (Regex.IsMatch(hcl, @"[""']\*:\*[""']", RegexOptions.IgnoreCase) ||
            Regex.IsMatch(hcl, @"actions?\s*=\s*\[[^\]]*[""']\*[""']", RegexOptions.IgnoreCase))
        {
            findings.Add(new SecurityFinding("IAM_STAR_STAR", "IAM policies must not grant wildcard actions.", "CRITICAL"));
        }

        return findings;
    }
}
