using InfraAgent.Tools.Terraform;
using Microsoft.Extensions.Logging;

namespace InfraAgent.Core.Provisioning;

public sealed class TerraformProvisioner(
    ITerraformRunner terraformRunner,
    ILogger<TerraformProvisioner> logger) : IInfrastructureProvisioner
{
    public async Task<ProvisioningResult> ProvisionAsync(string workingDirectory, CancellationToken cancellationToken)
    {
        logger.LogInformation("Applying Terraform in {WorkingDirectory}", workingDirectory);

        try
        {
            var apply = await terraformRunner.ApplyAsync(workingDirectory, cancellationToken);
            return apply.Succeeded
                ? ProvisioningResult.Success(apply.CombinedOutput)
                : ProvisioningResult.Failure(apply.CombinedOutput);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or FileNotFoundException)
        {
            return ProvisioningResult.Failure($"terraform is required and must be available on PATH: {ex.Message}");
        }
    }
}
