using InfraAgent.Tools.Processes;

namespace InfraAgent.Tools.Terraform;

public interface ITerraformRunner
{
    Task<CommandResult> FormatAsync(string workingDirectory, CancellationToken cancellationToken);

    Task<CommandResult> InitAsync(string workingDirectory, CancellationToken cancellationToken);

    Task<CommandResult> ValidateAsync(string workingDirectory, CancellationToken cancellationToken);

    Task<CommandResult> PlanAsync(string workingDirectory, bool refresh, CancellationToken cancellationToken);

    Task<CommandResult> ApplyAsync(string workingDirectory, CancellationToken cancellationToken);
}
