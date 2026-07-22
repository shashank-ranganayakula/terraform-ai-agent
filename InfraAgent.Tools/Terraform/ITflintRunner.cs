using InfraAgent.Tools.Processes;

namespace InfraAgent.Tools.Terraform;

public interface ITflintRunner
{
    Task<CommandResult> LintAsync(string workingDirectory, CancellationToken cancellationToken);
}
