using InfraAgent.Core.Context;
using InfraAgent.Core.Intent;

namespace InfraAgent.Core.Generation;

public interface ITerraformGenerator
{
    Task<GeneratedTerraform> GenerateAsync(
        InfrastructureIntent intent,
        IReadOnlyList<ContextDocument> context,
        string? repairInstructions,
        CancellationToken cancellationToken);
}
