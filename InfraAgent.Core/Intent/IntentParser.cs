using System.Text.RegularExpressions;
using InfraAgent.Core.Options;
using Microsoft.Extensions.Options;

namespace InfraAgent.Core.Intent;

public sealed class IntentParser(IOptions<AgentOptions> options) : IIntentParser
{
    private static readonly Regex CidrPattern = new(@"\b(?:\d{1,3}\.){3}\d{1,3}/(?:[0-9]|[12][0-9]|3[0-2])\b", RegexOptions.Compiled);
    private static readonly Regex RegionPattern = new(@"\b(?:us-gov|[a-z]{2})-[a-z]+(?:-[a-z]+)*-\d+\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex CredentialsPattern = new(@"\b(aws_access_key_id|aws_secret_access_key|aws_session_token|secret\s+access\s+key|access\s+key\s+id|private\s+key)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex DestructiveOperationPattern = new(@"\b(destroy|delete|remove|terminate|tear\s+down|decommission|wipe)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex UnsupportedProviderPattern = new(@"\b(azure|gcp|google\s+cloud|kubernetes|aks|gke|lambda|rds|dynamodb|eks|ecs|iam)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public IntentParseResult Parse(string prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt)) return IntentParseResult.Clarify("What AWS infrastructure should I generate?");
        if (prompt.Length > options.Value.MaxPromptCharacters)
            return IntentParseResult.Clarify($"Please shorten the request to {options.Value.MaxPromptCharacters} characters or fewer.");
        if (ContainsUnsupportedControlCharacters(prompt))
            return IntentParseResult.Clarify("The request contains unsupported control characters. Please remove them and try again.");
        if (CredentialsPattern.IsMatch(prompt))
            return IntentParseResult.Clarify("Do not include cloud credentials, tokens, or private keys in the prompt. Describe the infrastructure only.");
        if (DestructiveOperationPattern.IsMatch(prompt))
            return IntentParseResult.Clarify("Destructive operations are not supported. Describe only new AWS S3 or EC2 infrastructure to create.");
        if (UnsupportedProviderPattern.IsMatch(prompt))
            return IntentParseResult.Clarify("Phase 1 supports only AWS S3 buckets and EC2 instances. Remove unsupported services or providers.");

        var lower = prompt.ToLowerInvariant();
        var wantsS3 = lower.Contains("s3") || lower.Contains("bucket");
        var wantsEc2 = lower.Contains("ec2") || lower.Contains("instance") || lower.Contains("server");
        if (!wantsS3 && !wantsEc2) return IntentParseResult.Clarify("Phase 1 currently supports only AWS S3 and EC2.");
        if (wantsS3 && lower.Contains("public")) return IntentParseResult.Clarify("Public S3 buckets are disallowed. Remove public access or describe a private bucket.");

        var region = ExtractRegion(prompt);
        if (region is null)
            return IntentParseResult.Clarify("Which AWS region should I use? Include a valid region code such as us-east-1, us-west-2, eu-west-1, or ap-south-1.");
        if (!AwsRegionCatalog.IsKnownRegion(region))
            return IntentParseResult.Clarify($"'{region}' is not in the supported AWS region list. Use a valid AWS region code such as us-east-1, us-west-2, eu-west-1, or ap-south-1.");

        if (prompt.Contains("::/0", StringComparison.Ordinal))
            return IntentParseResult.Clarify("Open internet ingress is disallowed. Provide a restricted CIDR block.");

        var requestedCidr = CidrPattern.Match(prompt).Value;
        if (!string.IsNullOrWhiteSpace(requestedCidr) && !IsValidIpv4Cidr(requestedCidr))
            return IntentParseResult.Clarify($"'{requestedCidr}' is not a valid AWS CIDR. Use a network CIDR such as 10.0.0.0/8.");
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
        return IntentParseResult.Complete(new InfrastructureIntent(prompt, region, s3, ec2, ["Public access blocked.", "AES256 encryption enabled."]));
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
    private static string? ExtractRegion(string prompt) =>
        RegionPattern.Match(prompt) is { Success: true } match
            ? match.Value.ToLowerInvariant()
            : null;

    private static bool ContainsUnsupportedControlCharacters(string prompt) =>
        prompt.Any(character => char.IsControl(character) && character is not '\r' and not '\n' and not '\t');

    private static bool IsValidIpv4Cidr(string cidr)
    {
        var address = cidr.Split('/')[0];
        return address
            .Split('.')
            .All(octet => int.TryParse(octet, out var value) && value is >= 0 and <= 255);
    }
}
