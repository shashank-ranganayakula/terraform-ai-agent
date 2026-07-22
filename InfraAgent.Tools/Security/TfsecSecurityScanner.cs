using System.Text.Json;
using InfraAgent.Tools.Processes;
using Microsoft.Extensions.Logging;

namespace InfraAgent.Tools.Security;

public sealed class TfsecSecurityScanner(
    IProcessRunner processRunner,
    DeterministicSecurityPolicy policy,
    ILogger<TfsecSecurityScanner> logger) : ISecurityScanner
{
    public async Task<SecurityScanResult> ScanAsync(string workingDirectory, CancellationToken cancellationToken)
    {
        var findings = policy.EvaluateDirectory(workingDirectory).ToList();
        CommandResult tfsec;

        try
        {
            tfsec = await processRunner.RunAsync("tfsec", "--format json --no-color .", workingDirectory, cancellationToken);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or FileNotFoundException)
        {
            findings.Add(new SecurityFinding("TFSEC_UNAVAILABLE", "tfsec is required on PATH for Phase 1 validation.", "HIGH"));
            return new SecurityScanResult(findings);
        }

        var parsedTfsecOutput = false;
        if (!string.IsNullOrWhiteSpace(tfsec.StandardOutput))
        {
            var parsed = ParseTfsecFindings(tfsec.StandardOutput);
            parsedTfsecOutput = parsed.WasParseable;
            findings.AddRange(parsed.Findings);
        }

        if (!tfsec.Succeeded && findings.Count == 0 && !parsedTfsecOutput)
        {
            logger.LogWarning("tfsec failed without parseable findings: {Output}", tfsec.CombinedOutput);
            findings.Add(new SecurityFinding("TFSEC_FAILED", tfsec.CombinedOutput, "HIGH"));
        }

        return new SecurityScanResult(findings);
    }

    private static (bool WasParseable, IReadOnlyList<SecurityFinding> Findings) ParseTfsecFindings(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array)
        {
            return (false, Array.Empty<SecurityFinding>());
        }

        var findings = new List<SecurityFinding>();
        foreach (var result in results.EnumerateArray())
        {
            var ruleId = GetString(result, "rule_id") ?? "TFSEC";
            var description = GetString(result, "description") ?? GetString(result, "long_id") ?? "tfsec finding";
            var severity = GetString(result, "severity") ?? "HIGH";

            if (IsPhaseOneBlockingFinding(ruleId, description))
            {
                findings.Add(new SecurityFinding(ruleId, description, severity.ToUpperInvariant()));
            }
        }

        return (true, findings);
    }

    private static bool IsPhaseOneBlockingFinding(string ruleId, string description)
    {
        var text = $"{ruleId} {description}".ToLowerInvariant();
        var isS3Public = text.Contains("s3") && text.Contains("public");
        var isS3Encryption = text.Contains("s3") && (text.Contains("encryption") || text.Contains("unencrypted"));
        var isOpenIngress = text.Contains("ingress") &&
            (text.Contains("0.0.0.0/0") || text.Contains("::/0") || text.Contains("public internet"));
        var isWildcardIam = text.Contains("iam") && (text.Contains("wildcard") || text.Contains("*:*"));

        return isS3Public || isS3Encryption || isOpenIngress || isWildcardIam;
    }

    private static string? GetString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
}
