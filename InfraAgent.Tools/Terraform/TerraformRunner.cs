using InfraAgent.Tools.Processes;

namespace InfraAgent.Tools.Terraform;

public sealed class TerraformRunner(IProcessRunner processRunner) : ITerraformRunner
{
    private static readonly Lazy<IReadOnlyDictionary<string, string>> TerraformEnvironment = new(BuildTerraformEnvironment);

    public Task<CommandResult> FormatAsync(string workingDirectory, CancellationToken cancellationToken) =>
        RunTerraformAsync("fmt -recursive -no-color", workingDirectory, cancellationToken);

    public Task<CommandResult> InitAsync(string workingDirectory, CancellationToken cancellationToken) =>
        RunTerraformAsync("init -backend=false -input=false -no-color", workingDirectory, cancellationToken);

    public Task<CommandResult> ValidateAsync(string workingDirectory, CancellationToken cancellationToken) =>
        RunTerraformAsync("validate -no-color", workingDirectory, cancellationToken);

    public Task<CommandResult> PlanAsync(string workingDirectory, bool refresh, CancellationToken cancellationToken)
    {
        var refreshFlag = refresh ? "-refresh=true" : "-refresh=false";
        return RunTerraformAsync($"plan {refreshFlag} -input=false -no-color", workingDirectory, cancellationToken);
    }

    public Task<CommandResult> ApplyAsync(string workingDirectory, CancellationToken cancellationToken) =>
        RunTerraformAsync("apply -input=false -auto-approve -no-color", workingDirectory, cancellationToken);

    private Task<CommandResult> RunTerraformAsync(string arguments, string workingDirectory, CancellationToken cancellationToken) =>
        processRunner.RunAsync("terraform", arguments, workingDirectory, cancellationToken, TerraformEnvironment.Value);

    private static IReadOnlyDictionary<string, string> BuildTerraformEnvironment()
    {
        var configuredCache = Environment.GetEnvironmentVariable("TF_PLUGIN_CACHE_DIR");
        var cacheDirectory = string.IsNullOrWhiteSpace(configuredCache)
            ? Path.Combine(Environment.CurrentDirectory, "terraform-plugin-cache")
            : configuredCache;

        Directory.CreateDirectory(cacheDirectory);
        return new Dictionary<string, string>
        {
            ["TF_PLUGIN_CACHE_DIR"] = cacheDirectory
        };
    }
}
