using System.Text;
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
        if (!format.Succeeded)
        {
            return ValidationResult.Failure(output.ToString());
        }

        var init = await terraformRunner.InitAsync(workingDirectory, cancellationToken);
        output.AppendLine(init.CombinedOutput);
        if (!init.Succeeded)
        {
            return ValidationResult.Failure(output.ToString());
        }

        var validate = await terraformRunner.ValidateAsync(workingDirectory, cancellationToken);
        output.AppendLine(validate.CombinedOutput);
        if (!validate.Succeeded)
        {
            return ValidationResult.Failure(output.ToString());
        }

        var lint = await tflintRunner.LintAsync(workingDirectory, cancellationToken);
        output.AppendLine(lint.CombinedOutput);
        if (!lint.Succeeded)
        {
            return ValidationResult.Failure(output.ToString());
        }

        var scan = await securityScanner.ScanAsync(workingDirectory, cancellationToken);
        if (!scan.Passed)
        {
            output.AppendLine(scan.ToErrorText());
            return ValidationResult.Failure(output.ToString());
        }

        var plan = await terraformRunner.PlanAsync(workingDirectory, refresh: false, cancellationToken);
        output.AppendLine(plan.CombinedOutput);
        if (plan.Succeeded || IsMissingAwsCredentialsPlanFailure(plan.CombinedOutput))
        {
            return ValidationResult.Success(output.ToString());
        }

        return ValidationResult.Failure(output.ToString());
    }

    private static bool IsMissingAwsCredentialsPlanFailure(string output) =>
        output.Contains("No valid credential sources found", StringComparison.OrdinalIgnoreCase) ||
        output.Contains("no EC2 IMDS role found", StringComparison.OrdinalIgnoreCase);
}
