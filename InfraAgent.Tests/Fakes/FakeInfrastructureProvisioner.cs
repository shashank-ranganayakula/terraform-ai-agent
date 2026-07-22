using InfraAgent.Core.Provisioning;

namespace InfraAgent.Tests.Fakes;

public sealed class FakeInfrastructureProvisioner(params ProvisioningResult[] results) : IInfrastructureProvisioner
{
    private int _index;

    public int Calls => _index;

    public Task<ProvisioningResult> ProvisionAsync(string workingDirectory, CancellationToken cancellationToken)
    {
        var next = results.Length == 0
            ? ProvisioningResult.Success("applied")
            : results[Math.Min(_index, results.Length - 1)];
        _index++;
        return Task.FromResult(next);
    }
}
