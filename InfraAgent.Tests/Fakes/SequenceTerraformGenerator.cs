using InfraAgent.Core.Context;
using InfraAgent.Core.Generation;
using InfraAgent.Core.Intent;

namespace InfraAgent.Tests.Fakes;

public sealed class SequenceTerraformGenerator(params GeneratedTerraform[] generations) : ITerraformGenerator
{
    private int _index;

    public int Calls => _index;

    public Task<GeneratedTerraform> GenerateAsync(
        InfrastructureIntent intent,
        IReadOnlyList<ContextDocument> context,
        string? repairInstructions,
        CancellationToken cancellationToken)
    {
        var next = generations[Math.Min(_index, generations.Length - 1)];
        _index++;
        return Task.FromResult(next);
    }
}
