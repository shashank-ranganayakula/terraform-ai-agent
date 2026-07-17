namespace InfraAgent.Tools.Security;

public sealed record SecurityScanResult(IReadOnlyList<SecurityFinding> Findings)
{
    public bool Passed => Findings.Count == 0;

    public string ToErrorText() => string.Join(
        Environment.NewLine,
        Findings.Select(finding => $"{finding.Severity} {finding.Code}: {finding.Message}"));
}
