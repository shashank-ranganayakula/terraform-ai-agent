namespace InfraAgent.Tools.Security;

public interface ISecurityScanner
{
    Task<SecurityScanResult> ScanAsync(string workingDirectory, CancellationToken cancellationToken);
}
