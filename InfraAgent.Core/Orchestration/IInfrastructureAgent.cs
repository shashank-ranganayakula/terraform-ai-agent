namespace InfraAgent.Core.Orchestration;

public interface IInfrastructureAgent
{
    Task<GenerateResponse> GenerateAsync(string prompt, CancellationToken cancellationToken);
}
