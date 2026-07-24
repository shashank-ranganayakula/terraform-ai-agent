using InfraAgent.Core.Context;
using InfraAgent.Core.Generation;
using InfraAgent.Core.Intent;
using InfraAgent.Core.Options;
using InfraAgent.Core.Preflight;
using InfraAgent.Core.Provisioning;
using InfraAgent.Core.Validation;
using InfraAgent.Tools.Git;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InfraAgent.Core.Orchestration;

public sealed class InfrastructureAgent(
    IIntentParser intentParser,
    IContextRetriever contextRetriever,
    ITerraformGenerator terraformGenerator,
    IInfrastructureValidator validator,
    IInfrastructureProvisioner provisioner,
    IS3BucketAvailabilityChecker s3BucketAvailabilityChecker,
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
        var requestStopwatch = Stopwatch.StartNew();

        if (intent.S3Bucket is { } bucket)
        {
            var preflightStopwatch = Stopwatch.StartNew();
            var availability = await s3BucketAvailabilityChecker.CheckAsync(
                bucket.BucketName,
                intent.AwsRegion,
                cancellationToken);
            preflightStopwatch.Stop();

            logger.LogInformation(
                "S3 bucket preflight completed in {ElapsedMilliseconds} ms with status {Status}",
                preflightStopwatch.ElapsedMilliseconds,
                availability.Status);

            if (availability.Status == S3BucketAvailabilityStatus.Exists)
            {
                return GenerateResponse.PreflightFailure(
                    "S3 bucket name already exists.",
                    availability.Message);
            }

            if (availability.Status == S3BucketAvailabilityStatus.CheckFailed)
            {
                return GenerateResponse.PreflightFailure(
                    "S3 bucket availability check failed.",
                    availability.Message);
            }
        }

        var contextStopwatch = Stopwatch.StartNew();
        var context = await contextRetriever.RetrieveAsync(intent, cancellationToken);
        contextStopwatch.Stop();
        logger.LogInformation("Context retrieval completed in {ElapsedMilliseconds} ms", contextStopwatch.ElapsedMilliseconds);

        string? repairInstructions = null;
        ValidationResult? finalValidation = null;
        GeneratedTerraform? finalTerraform = null;
        var finalDirectory = CreateWorkingDirectory();

        for (var attempt = 1; attempt <= options.Value.MaxRepairAttempts; attempt++)
        {
            logger.LogInformation("Generating Terraform attempt {Attempt} of {MaxAttempts}", attempt, options.Value.MaxRepairAttempts);
            var generationStopwatch = Stopwatch.StartNew();
            var terraform = TerraformVariablePruner.PruneUnusedVariables(
                TerraformSecurityDefaults.EnsureS3Defaults(
                    await terraformGenerator.GenerateAsync(intent, context, repairInstructions, cancellationToken)));
            generationStopwatch.Stop();
            logger.LogInformation(
                "Terraform generation attempt {Attempt} completed in {ElapsedMilliseconds} ms",
                attempt,
                generationStopwatch.ElapsedMilliseconds);

            ClearGeneratedSourceFiles(finalDirectory);
            await WriteTerraformFilesAsync(finalDirectory, terraform, cancellationToken);

            var validationStopwatch = Stopwatch.StartNew();
            var validation = await validator.ValidateAsync(finalDirectory, cancellationToken);
            validationStopwatch.Stop();
            logger.LogInformation(
                "Validation attempt {Attempt} completed in {ElapsedMilliseconds} ms with status {Status}",
                attempt,
                validationStopwatch.ElapsedMilliseconds,
                validation.Succeeded ? "succeeded" : "failed");
            if (validation.Succeeded)
            {
                finalTerraform = terraform;
                finalValidation = validation;
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
        var assumptions = finalTerraform.Assumptions
            .Concat(intent.Assumptions)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        RepositoryPublishResult publishResult;
        try
        {
            var publishStopwatch = Stopwatch.StartNew();
            publishResult = await gitRepository.PublishAsync(
                new RepositoryPublishRequest(
                    finalDirectory,
                    repoName,
                    $"infra-agent: generate {string.Join(",", intent.ResourceKinds)}",
                    finalTerraform.Summary),
                cancellationToken);
            publishStopwatch.Stop();
            logger.LogInformation("Repository publishing completed in {ElapsedMilliseconds} ms", publishStopwatch.ElapsedMilliseconds);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogError(ex, "Repository publishing failed.");
            return GenerateResponse.PublishFailure(
                "Repository publishing failed.",
                ex.Message,
                finalTerraform.Summary,
                assumptions);
        }

        var provisioningStopwatch = Stopwatch.StartNew();
        var provisioning = await provisioner.ProvisionAsync(finalDirectory, cancellationToken);
        provisioningStopwatch.Stop();
        logger.LogInformation("Terraform provisioning completed in {ElapsedMilliseconds} ms", provisioningStopwatch.ElapsedMilliseconds);
        if (!provisioning.Succeeded)
        {
            return GenerateResponse.ProvisioningFailure(
                "Terraform apply failed.",
                provisioning.Output,
                publishResult.RepositoryUrl,
                publishResult.Files,
                finalTerraform.Summary,
                assumptions);
        }

        requestStopwatch.Stop();
        logger.LogInformation("Generate request completed in {ElapsedMilliseconds} ms", requestStopwatch.ElapsedMilliseconds);

        return GenerateResponse.Success(
            publishResult.RepositoryUrl,
            publishResult.Files,
            finalTerraform.Summary,
            assumptions,
            provisioning.Output);
    }

    private string CreateWorkingDirectory()
    {
        var directory = Path.Combine(options.Value.WorkingRoot, DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmssfff"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void ClearGeneratedSourceFiles(string workingDirectory)
    {
        if (!Directory.Exists(workingDirectory))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(workingDirectory, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(workingDirectory, file);
            if (IsPreservedTerraformPath(relativePath))
            {
                continue;
            }

            File.Delete(file);
        }

        foreach (var directory in Directory
            .EnumerateDirectories(workingDirectory, "*", SearchOption.AllDirectories)
            .OrderByDescending(path => path.Length))
        {
            var relativePath = Path.GetRelativePath(workingDirectory, directory);
            if (IsPreservedTerraformPath(relativePath))
            {
                continue;
            }

            if (!Directory.EnumerateFileSystemEntries(directory).Any())
            {
                Directory.Delete(directory);
            }
        }
    }

    private static bool IsPreservedTerraformPath(string relativePath)
    {
        var normalized = relativePath.Replace('\\', '/');
        return normalized.Equals(".terraform.lock.hcl", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals(".terraform", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith(".terraform/", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals(".git", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith(".git/", StringComparison.OrdinalIgnoreCase);
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
        await File.WriteAllTextAsync(Path.Combine(workingDirectory, ".gitignore"), ".terraform/" + Environment.NewLine + "*.tfstate" + Environment.NewLine + "*.tfstate.*" + Environment.NewLine, cancellationToken);
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
