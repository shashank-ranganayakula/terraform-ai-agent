namespace InfraAgent.Tools.Processes;

public sealed record CommandResult(
    string FileName,
    string Arguments,
    int ExitCode,
    string StandardOutput,
    string StandardError)
{
    public bool Succeeded => ExitCode == 0;

    public string CombinedOutput => string.Join(
        Environment.NewLine,
        new[] { StandardOutput, StandardError }.Where(value => !string.IsNullOrWhiteSpace(value)));
}
