using InfraAgent.Core.Context;
using InfraAgent.Core.Generation;
using InfraAgent.Core.Intent;
using InfraAgent.Core.Options;
using InfraAgent.Core.Validation;
using InfraAgent.Tools.Git;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InfraAgent.Core.Orchestration;

public sealed class InfrastructureAgent(
    IIntentParser intentParser,
    IContextRetriever contextRetriever,
    ITerraformGenerator terraformGenerator,
    IInfrastructureValidator validator,
    IGitRepository gitRepository,
    IOptions<AgentOptions> options,
    ILogger<InfrastructureAgent> logger) : IInfrastructureAgent
{
    public async Task<GenerateResponse> GenerateAsync(string prompt, CancellationToken cancellationToken)
    {
        var parseResult = intentParser.Parse(prompt);
        if (parseResult.NeedsClarification)
        {
            return GenerateResponse.Clarification(parseResult.ClarifyingQuestion!);
        }

        var intent = parseResult.Intent!;
        var context = await contextRetriever.RetrieveAsync(intent, cancellationToken);
        string? repairInstructions = null;
        ValidationResult? finalValidation = null;
        GeneratedTerraform? finalTerraform = null;
        string? finalDirectory = null;

        for (var attempt = 1; attempt <= options.Value.MaxRepairAttempts; attempt++)
        {
            logger.LogInformation("Generating Terraform attempt {Attempt} of {MaxAttempts}", attempt, options.Value.MaxRepairAttempts);
            var terraform = await terraformGenerator.GenerateAsync(intent, context, repairInstructions, cancellationToken);
            var workingDirectory = CreateWorkingDirectory();
            await WriteTerraformFilesAsync(workingDirectory, terraform, cancellationToken);

            var validation = await validator.ValidateAsync(workingDirectory, cancellationToken);
            if (validation.Succeeded)
            {
                finalTerraform = terraform;
                finalValidation = validation;
                finalDirectory = workingDirectory;
                break;
            }

            finalValidation = validation;
            repairInstructions = $"The previous Terraform failed validation. Fix only the Terraform and keep Phase 1 scope. Errors:{Environment.NewLine}{validation.Output}";
        }

        if (finalTerraform is null || finalDirectory is null || finalValidation is null || !finalValidation.Succeeded)
        {
            return GenerateResponse.Failure(finalValidation?.Output ?? "Generation failed before validation.");
        }

        await WriteRepositoryFilesAsync(finalDirectory, finalTerraform, cancellationToken);
        var repoName = $"infra-agent-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";
        var publishResult = await gitRepository.PublishAsync(
            new RepositoryPublishRequest(
                finalDirectory,
                repoName,
                $"infra-agent: generate {string.Join(",", intent.ResourceKinds)}",
                finalTerraform.Summary),
            cancellationToken);

        return GenerateResponse.Success(
            publishResult.RepositoryUrl,
            publishResult.Files,
            finalTerraform.Summary,
            finalTerraform.Assumptions.Concat(intent.Assumptions).Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
    }

    private string CreateWorkingDirectory()
    {
        var directory = Path.Combine(options.Value.WorkingRoot, DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmssfff"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static async Task WriteTerraformFilesAsync(string workingDirectory, GeneratedTerraform terraform, CancellationToken cancellationToken)
    {
        foreach (var file in terraform.Files)
        {
            var safePath = file.Key.Replace('\\', '/');
            if (safePath.StartsWith('/') || safePath.Contains("..", StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Generated file path '{file.Key}' is not allowed.");
            }

            var destination = Path.Combine(workingDirectory, safePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            await File.WriteAllTextAsync(destination, file.Value, cancellationToken);
        }
    }

    private static async Task WriteRepositoryFilesAsync(string workingDirectory, GeneratedTerraform terraform, CancellationToken cancellationToken)
    {
        await File.WriteAllTextAsync(Path.Combine(workingDirectory, "README.md"), BuildGeneratedReadme(terraform), cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(workingDirectory, ".gitignore"), ".terraform/" + Environment.NewLine + "*.tfstate" + Environment.NewLine + "*.tfstate.*" + Environment.NewLine + ".terraform.lock.hcl" + Environment.NewLine, cancellationToken);
    }

    private static string BuildGeneratedReadme(GeneratedTerraform terraform)
    {
        var assumptions = terraform.Assumptions.Count == 0
            ? "None."
            : string.Join(Environment.NewLine, terraform.Assumptions.Select(assumption => $"- {assumption}"));

        return $"""
        # Generated Infrastructure

        {terraform.Summary}

        ## Assumptions

        {assumptions}

        ## Validate Locally

        ```powershell
        terraform init -backend=false
        terraform fmt -recursive
        terraform validate
        tflint --no-color
        terraform plan -refresh=false
        tfsec .
        ```
        """;
    }
}
