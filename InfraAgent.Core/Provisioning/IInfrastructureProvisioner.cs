namespace InfraAgent.Core.Provisioning;

public interface IInfrastructureProvisioner
{
    Task<ProvisioningResult> ProvisionAsync(string workingDirectory, CancellationToken cancellationToken);
}
