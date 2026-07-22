using InfraAgent.Core.Intent;

namespace InfraAgent.Core.Context;

public interface IContextRetriever
{
    Task<IReadOnlyList<ContextDocument>> RetrieveAsync(InfrastructureIntent intent, CancellationToken cancellationToken);
}
