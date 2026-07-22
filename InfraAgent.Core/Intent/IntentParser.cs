using System.Text.RegularExpressions;
using InfraAgent.Core.Options;
using Microsoft.Extensions.Options;

namespace InfraAgent.Core.Intent;

public sealed class IntentParser(IOptions<AgentOptions> options) : IIntentParser
{
    private static readonly Regex CidrPattern = new(@"\b(?:\d{1,3}\.){3}\d{1,3}/(?:[0-9]|[12][0-9]|3[0-2])\b", RegexOptions.Compiled);
    public IntentParseResult Parse(string prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt)) return IntentParseResult.Clarify("What AWS infrastructure should I generate?");
        var lower = prompt.ToLowerInvariant();
        var wantsS3 = lower.Contains("s3") || lower.Contains("bucket");
        var wantsEc2 = lower.Contains("ec2") || lower.Contains("instance") || lower.Contains("server");
        if (!wantsS3 && !wantsEc2) return IntentParseResult.Clarify("Phase 1 currently supports only AWS S3 and EC2.");
        if (wantsS3 && lower.Contains("public")) return IntentParseResult.Clarify("Public S3 buckets are disallowed. Remove public access or describe a private bucket.");
        var requestedCidr = CidrPattern.Match(prompt).Value;
        if (requestedCidr.EndsWith("/0", StringComparison.Ordinal) && requestedCidr != "0.0.0.0/0")
            return IntentParseResult.Clarify($"'{requestedCidr}' is not a valid AWS CIDR. Use a network CIDR such as 10.0.0.0/8.");
        if (requestedCidr == "0.0.0.0/0" || requestedCidr == "::/0")
            return IntentParseResult.Clarify("Open internet ingress is disallowed. Provide a restricted CIDR block.");
        var s3 = wantsS3 ? new S3BucketIntent("uploads", ExtractBucketName(prompt), lower.Contains("version"), true, !lower.Contains("without encryption")) : null;
        if (wantsEc2)
        {
            var instanceType = ExtractInstanceType(prompt);
            if (instanceType is null)
                return IntentParseResult.Clarify($"Which EC2 instance type? Allowed: {string.Join(", ", options.Value.AllowedEc2InstanceTypes)}");
            if (!options.Value.AllowedEc2InstanceTypes.Contains(instanceType, StringComparer.OrdinalIgnoreCase))
                return IntentParseResult.Clarify($"Requested EC2 instance type is outside the allowlist. Allowed: {string.Join(", ", options.Value.AllowedEc2InstanceTypes)}");
            if ((lower.Contains("ssh") || lower.Contains("http") || lower.Contains("https")) && !CidrPattern.IsMatch(prompt))
                return IntentParseResult.Clarify("Which CIDR block should be allowed for the requested ingress?");
        }
        var ec2 = wantsEc2 ? ParseEc2(prompt) : null;
        if (wantsEc2 && ec2 is null) return IntentParseResult.Clarify($"Which EC2 instance type? Allowed: {string.Join(", ", options.Value.AllowedEc2InstanceTypes)}");
        return IntentParseResult.Complete(new InfrastructureIntent(prompt, ExtractRegion(prompt) ?? options.Value.DefaultAwsRegion, s3, ec2, ["Public access blocked.", "AES256 encryption enabled."]));
    }
    private Ec2InstanceIntent? ParseEc2(string prompt)
    {
        var instanceType = ExtractInstanceType(prompt);
        if (instanceType is null || !options.Value.AllowedEc2InstanceTypes.Contains(instanceType, StringComparer.OrdinalIgnoreCase)) return null;
        var normalized = prompt.ToLowerInvariant();
        var cidr = CidrPattern.Match(prompt);
        if ((normalized.Contains("ssh") || normalized.Contains("http") || normalized.Contains("https")) && !cidr.Success)
            return null;

        var rules = new List<IngressRuleIntent>();
        if (cidr.Success)
        {
            var port = normalized.Contains("ssh") ? 22 : normalized.Contains("https") ? 443 : 80;
            rules.Add(new IngressRuleIntent(port, port, "tcp", cidr.Value, port switch { 22 => "SSH", 443 => "HTTPS", _ => "HTTP" }));
        }

        return new Ec2InstanceIntent("web", instanceType, "al2023-ami-2023.*-x86_64", rules, "web-server");
    }
    private static string? ExtractInstanceType(string prompt)
    {
        var match = Regex.Match(prompt, @"\b[a-z][0-9][a-z0-9]*\.[a-z0-9]+(?:\.[a-z0-9]+)?\b", RegexOptions.IgnoreCase);
        return match.Success ? match.Value : null;
    }
    private static string ExtractBucketName(string prompt)
    {
        var match = Regex.Match(prompt, @"(?:bucket\s+(?:named|called)|named|called|bucket\s+name\s*(?:is|should\s*be)?)\s*[:=]?\s*([a-z0-9][a-z0-9.-]{2,62})(?=$|[\s,.;])", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : "infra-agent-bucket";
    }
    private static string? ExtractRegion(string prompt) => Regex.Match(prompt, @"\b(?:us|eu|ap|sa|ca|af|me)-[a-z]+-\d\b", RegexOptions.IgnoreCase) is { Success: true } m ? m.Value : null;
}
