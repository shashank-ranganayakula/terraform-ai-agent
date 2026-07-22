namespace InfraAgent.Core.Provisioning;

public sealed record ProvisioningResult(bool Succeeded, string Output)
{
    public static ProvisioningResult Success(string output) => new(true, output);

    public static ProvisioningResult Failure(string output) => new(false, output);
}
