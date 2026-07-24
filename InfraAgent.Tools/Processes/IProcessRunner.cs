namespace InfraAgent.Tools.Processes;

public interface IProcessRunner
{
    Task<CommandResult> RunAsync(
        string fileName,
        string arguments,
        string workingDirectory,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<string, string>? environmentVariables = null);
}
