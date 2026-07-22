using System.Text;
using InfraAgent.Tools.Processes;
using InfraAgent.Tools.Security;
using InfraAgent.Tools.Terraform;

namespace InfraAgent.Core.Validation;

public sealed class InfrastructureValidator(
    ITerraformRunner terraformRunner,
    ITflintRunner tflintRunner,
    ISecurityScanner securityScanner) : IInfrastructureValidator
{
    public async Task<ValidationResult> ValidateAsync(string workingDirectory, CancellationToken cancellationToken)
    {
        var output = new StringBuilder();
        var format = await terraformRunner.FormatAsync(workingDirectory, cancellationToken);
        output.AppendLine(format.CombinedOutput);
        if (!format.Succeeded) return ValidationResult.Failure(output.ToString());
        var init = await terraformRunner.InitAsync(workingDirectory, cancellationToken);
        output.AppendLine(init.CombinedOutput);
        if (!init.Succeeded) return ValidationResult.Failure(output.ToString());
        var validate = await terraformRunner.ValidateAsync(workingDirectory, cancellationToken);
        output.AppendLine(validate.CombinedOutput);
        if (!validate.Succeeded) return ValidationResult.Failure(output.ToString());
        CommandResult lint;
        try
        {
            lint = await tflintRunner.LintAsync(workingDirectory, cancellationToken);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or FileNotFoundException)
        {
            return ValidationResult.Failure($"tflint is required and must be available on PATH: {ex.Message}");
        }
        output.AppendLine(lint.CombinedOutput);
        if (!lint.Succeeded) return ValidationResult.Failure(output.ToString());
        var scan = await securityScanner.ScanAsync(workingDirectory, cancellationToken);
        if (!scan.Passed) { output.AppendLine(scan.ToErrorText()); return ValidationResult.Failure(output.ToString()); }
        output.AppendLine("Terraform plan skipped; generation-only mode is enabled.");
        return ValidationResult.Success(output.ToString());
    }
}
