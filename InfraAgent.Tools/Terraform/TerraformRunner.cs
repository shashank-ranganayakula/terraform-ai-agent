using InfraAgent.Tools.Processes;

namespace InfraAgent.Tools.Terraform;

public sealed class TerraformRunner(IProcessRunner processRunner) : ITerraformRunner
{
    public Task<CommandResult> FormatAsync(string workingDirectory, CancellationToken cancellationToken) =>
        processRunner.RunAsync("terraform", "fmt -recursive -no-color", workingDirectory, cancellationToken);

    public Task<CommandResult> InitAsync(string workingDirectory, CancellationToken cancellationToken) =>
        processRunner.RunAsync("terraform", "init -backend=false -input=false -no-color", workingDirectory, cancellationToken);

    public Task<CommandResult> ValidateAsync(string workingDirectory, CancellationToken cancellationToken) =>
        processRunner.RunAsync("terraform", "validate -no-color", workingDirectory, cancellationToken);

    public Task<CommandResult> PlanAsync(string workingDirectory, bool refresh, CancellationToken cancellationToken)
    {
        var refreshFlag = refresh ? "-refresh=true" : "-refresh=false";
        return processRunner.RunAsync("terraform", $"plan {refreshFlag} -input=false -no-color", workingDirectory, cancellationToken);
    }
}
