using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace InfraAgent.Tools.Processes;

public sealed class ProcessRunner(ILogger<ProcessRunner> logger) : IProcessRunner
{
    public async Task<CommandResult> RunAsync(
        string fileName,
        string arguments,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(workingDirectory);

        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        logger.LogInformation("Running command {FileName} {Arguments} in {WorkingDirectory}", fileName, arguments, workingDirectory);
        logger.LogInformation("Executable: {FileName}", fileName);
        logger.LogInformation("Exists: {Exists}", File.Exists(fileName));
        process.Start();
        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        return new CommandResult(
            fileName,
            arguments,
            process.ExitCode,
            await stdoutTask,
            await stderrTask);
    }
}
