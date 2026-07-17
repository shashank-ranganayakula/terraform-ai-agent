using System.Text.RegularExpressions;
using InfraAgent.Core.Options;
using Microsoft.Extensions.Options;

namespace InfraAgent.Core.Intent;

public sealed class IntentParser(IOptions<AgentOptions> options) : IIntentParser
{
    private static readonly Regex InstanceTypePattern = new(@"\b[a-z][0-9][a-z0-9]*\.[a-z0-9]+(?:\.[a-z0-9]+)?\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex CidrPattern = new(@"\b(?:\d{1,3}\.){3}\d{1,3}/\d{1,2}\b", RegexOptions.Compiled);

    public IntentParseResult Parse(string prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt))
        {
            return IntentParseResult.Clarify("What AWS infrastructure should I generate?");
        }

        var normalized = prompt.ToLowerInvariant();
        var wantsS3 = normalized.Contains("s3") || normalized.Contains("bucket");
        var wantsEc2 = normalized.Contains("ec2") || normalized.Contains("instance") || normalized.Contains("server");

        if (!wantsS3 && !wantsEc2)
        {
            return IntentParseResult.Clarify("Phase 1 supports AWS S3 buckets and EC2 instances only. Which of those should I create?");
        }

        if (wantsS3 && ContainsPublicAccessRequest(normalized))
        {
            return IntentParseResult.Clarify("Public S3 buckets are disallowed in Phase 1. Should I create the bucket with all public access blocked?");
        }

        if (ContainsIngressRequest(normalized) && !CidrPattern.IsMatch(prompt))
        {
            return IntentParseResult.Clarify("Which non-public CIDR block should be allowed for the requested EC2 ingress rule?");
        }

        var assumptions = new List<string>();
        S3BucketIntent? s3Bucket = null;
        if (wantsS3)
        {
            s3Bucket = new S3BucketIntent(
                LogicalName: "uploads",
                VersioningEnabled: normalized.Contains("versioning") || normalized.Contains("versioned"),
                BlockPublicAccess: true,
                ServerSideEncryptionEnabled: true);
            assumptions.Add("S3 public access is blocked by policy.");
            assumptions.Add("S3 server-side encryption uses AES256.");
        }

        Ec2InstanceIntent? ec2Instance = null;
        if (wantsEc2)
        {
            var instanceType = ExtractInstanceType(prompt);
            if (instanceType is null)
            {
                return IntentParseResult.Clarify($"Which EC2 instance type should I use? Allowed values: {string.Join(", ", options.Value.AllowedEc2InstanceTypes)}.");
            }

            if (!options.Value.AllowedEc2InstanceTypes.Contains(instanceType, StringComparer.OrdinalIgnoreCase))
            {
                return IntentParseResult.Clarify($"The requested instance type '{instanceType}' is not in the allowlist. Allowed values: {string.Join(", ", options.Value.AllowedEc2InstanceTypes)}.");
            }

            var ingressRules = ExtractIngressRules(prompt, normalized);
            if (ContainsIngressRequest(normalized) && ingressRules.Any(rule => rule.CidrBlock is "0.0.0.0/0" or "::/0"))
            {
                return IntentParseResult.Clarify("Ingress from 0.0.0.0/0 or ::/0 is disallowed. Which narrower CIDR block should I use?");
            }

            if (ContainsWebServerPurpose(normalized) && ingressRules.Count == 0)
            {
                assumptions.Add("No inbound security group ingress was opened because no non-public source CIDR was provided.");
            }

            ec2Instance = new Ec2InstanceIntent(
                LogicalName: "web",
                InstanceType: instanceType,
                AmiNamePattern: "al2023-ami-2023.*-x86_64",
                IngressRules: ingressRules);
        }

        var intent = new InfrastructureIntent(
            prompt,
            options.Value.DefaultAwsRegion,
            s3Bucket,
            ec2Instance,
            assumptions);

        return IntentParseResult.Complete(intent);
    }

    private string? ExtractInstanceType(string prompt)
    {
        var match = InstanceTypePattern.Match(prompt);
        if (!match.Success)
        {
            return null;
        }

        return match.Value.ToLowerInvariant();
    }

    private static IReadOnlyList<IngressRuleIntent> ExtractIngressRules(string prompt, string normalized)
    {
        var cidr = CidrPattern.Match(prompt);
        if (!cidr.Success)
        {
            return Array.Empty<IngressRuleIntent>();
        }

        if (normalized.Contains("ssh") || normalized.Contains("port 22"))
        {
            return [new IngressRuleIntent(22, 22, "tcp", cidr.Value, "SSH")];
        }

        if (normalized.Contains("https") || normalized.Contains("port 443"))
        {
            return [new IngressRuleIntent(443, 443, "tcp", cidr.Value, "HTTPS")];
        }

        if (normalized.Contains("http") || normalized.Contains("web") || normalized.Contains("port 80"))
        {
            return [new IngressRuleIntent(80, 80, "tcp", cidr.Value, "HTTP")];
        }

        return Array.Empty<IngressRuleIntent>();
    }

    private static bool ContainsPublicAccessRequest(string normalized) =>
        normalized.Contains("public bucket") ||
        normalized.Contains("public s3") ||
        normalized.Contains("public-read") ||
        normalized.Contains("public read") ||
        normalized.Contains("make it public");

    private static bool ContainsIngressRequest(string normalized) =>
        normalized.Contains("allow ssh") ||
        normalized.Contains("allow http") ||
        normalized.Contains("allow https") ||
        normalized.Contains("ingress") ||
        normalized.Contains("inbound") ||
        normalized.Contains("open port");

    private static bool ContainsWebServerPurpose(string normalized) =>
        normalized.Contains("web server") || normalized.Contains("run a web");
}
