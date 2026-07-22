using InfraAgent.Tools.Processes;

namespace InfraAgent.Tools.Terraform;

public sealed class TflintRunner(IProcessRunner processRunner) : ITflintRunner
{
    public Task<CommandResult> LintAsync(string workingDirectory, CancellationToken cancellationToken) =>
        processRunner.RunAsync("tflint", "--no-color", workingDirectory, cancellationToken);
}
