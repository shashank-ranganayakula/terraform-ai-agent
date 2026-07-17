# InfraAgent Phase 1

InfraAgent is a .NET 8 infrastructure-provisioning agent for the Phase 1 scope in [agents.md](agents.md). It accepts a natural-language request, parses it into a small AWS infrastructure intent, generates Terraform for supported S3 and EC2 resources, validates the output with deterministic and CLI-based checks, and publishes the generated Terraform to GitHub or to a local Git repository.

Phase 1 intentionally supports only:

- AWS S3 buckets with versioning, server-side encryption, and public access block resources.
- AWS EC2 instances with an Amazon Linux AMI lookup, an instance, and an associated security group.

It intentionally does not generate multi-cloud infrastructure, VPCs from scratch, RDS, Lambda, remote Terraform state backends, or CI/CD pipelines.

## Solution Structure

- `InfraAgent.sln` - Visual Studio/.NET solution tying together the API, Core, Tools, and Tests projects.
- `agents.md` - project scope and guardrails. Treat this as the product contract for Phase 1.
- `README.md` - this guide.
- `api-run*.log` - local runtime logs from previous API runs.
- `terraform-plugin-cache/` and `terraform-cache-smoke/` - local Terraform provider/cache artifacts used for validation smoke runs.

## Projects

- `InfraAgent.Api` - ASP.NET Core Minimal API. It wires configuration, dependency injection, Swagger, and HTTP endpoints.
- `InfraAgent.Core` - business logic: prompt parsing, context retrieval, Terraform generation, validation orchestration, and the repair loop.
- `InfraAgent.Tools` - external-tool adapters: process execution, Terraform CLI, tflint, tfsec, local Git publishing, and GitHub publishing.
- `InfraAgent.Tests` - xUnit tests for parsing, generation, guardrails, orchestration, and Terraform CLI integration.

## Runtime Flow

1. A client calls `POST /generate` with a JSON body containing a `prompt`.
2. `IntentParser` checks whether the prompt asks for supported S3 and/or EC2 resources.
3. If required details are missing or disallowed, the API returns `clarification_required` with a question.
4. `FileContextRetriever` loads AWS-specific context markdown from `InfraAgent.Core/Context`.
5. The selected Terraform generator runs:
   - `TemplateTerraformGenerator` is used when `OpenAI:ApiKey` is empty.
   - `OpenAiTerraformGenerator` is used when `OpenAI:ApiKey` is configured.
6. `InfrastructureAgent` writes generated Terraform to a timestamped folder under `Agent:WorkingRoot`.
7. `InfrastructureValidator` runs formatting, initialization, validation, linting, security scanning, and planning.
8. If validation fails, the agent retries generation with repair instructions up to `Agent:MaxRepairAttempts`.
9. On success, the agent writes a generated README and `.gitignore`.
10. The generated files are published:
    - `LocalGitRepository` is used when `Git:GitHubToken` is empty.
    - `GitHubRepository` is used when `Git:GitHubToken` is configured.
11. The API returns a `succeeded`, `clarification_required`, or failed/problem response.

## API

Swagger is enabled for manual testing at:

```text
http://localhost:5123/swagger
https://localhost:7112/swagger
```

The exact port depends on `InfraAgent.Api/Properties/launchSettings.json` and the `dotnet run` profile.

### `GET /`

Health check endpoint. Returns:

```json
{
  "service": "InfraAgent.Api",
  "status": "running"
}
```

### `POST /generate`

Generates, validates, and publishes Terraform.

Request:

```json
{
  "prompt": "Create an S3 bucket for user uploads with versioning enabled, and a t3.medium EC2 instance to run a web server"
}
```

Possible successful response statuses:

- `succeeded` - Terraform passed validation and was committed to a repository.
- `clarification_required` - the prompt was unsupported, unsafe, or missing required information.

Validation failure returns HTTP `422`.

## Configuration

Configuration lives in `InfraAgent.Api/appsettings.json` and can be overridden with environment variables.

```powershell
$env:OpenAI__ApiKey = "sk-your-openai-api-key"
$env:OpenAI__Model = "gpt-4.1-mini"
$env:OpenAI__BaseUrl = "https://aicredits.in/v1"
$env:Git__GitHubToken = "github_pat_your-token"
$env:Git__GitHubOwner = "your-org-or-user"
$env:Agent__DefaultAwsRegion = "us-east-1"
```

Important settings:

- `OpenAI:ApiKey` - enables model-backed Terraform generation. Leave empty for deterministic local demo mode.
- `OpenAI:Model` - model name used by `OpenAiTerraformGenerator`.
- `OpenAI:BaseUrl` - OpenAI-compatible API base URL. Defaults to `https://aicredits.in/v1` for the configured third-party provider.
- `Git:GitHubToken` - enables GitHub repository creation and push. Leave empty for local repository publishing.
- `Git:GitHubOwner` - optional GitHub owner/org for remote repository creation.
- `Git:UsePrivateRepositories` - controls whether GitHub repositories are created private.
- `Git:LocalRepositoryRoot` - local output folder used when GitHub publishing is disabled.
- `Agent:DefaultAwsRegion` - default AWS region injected into generated Terraform variables.
- `Agent:AllowedEc2InstanceTypes` - allowlist used by prompt parsing.
- `Agent:MaxRepairAttempts` - maximum generation/repair attempts.
- `Agent:WorkingRoot` - folder for generated Terraform validation workspaces.

Do not put AWS credentials in generated Terraform. Use normal AWS CLI/SDK environment configuration if you intend to run `terraform plan` against real AWS.

## How To Run

Install prerequisites:

- .NET 8 SDK and ASP.NET Core 8 runtime.
- `terraform` on `PATH`.
- `tflint` on `PATH`.
- `tfsec` on `PATH`.

Restore and run:

```powershell
dotnet restore
dotnet run --project InfraAgent.Api
```

Open Swagger in a browser:

```text
http://localhost:5123/swagger
```

Or call the API from PowerShell:

```powershell
Invoke-RestMethod -Method Post `
  -Uri http://localhost:5123/generate `
  -ContentType 'application/json' `
  -Body '{"prompt":"Create an S3 bucket for user uploads with versioning enabled, and a t3.medium EC2 instance to run a web server"}'
```

For an EC2 ingress example, include a non-public CIDR:

```powershell
Invoke-RestMethod -Method Post `
  -Uri http://localhost:5123/generate `
  -ContentType 'application/json' `
  -Body '{"prompt":"Create a t3.micro EC2 instance for SSH from 10.0.0.0/24"}'
```

If `Git:GitHubToken` is not configured, `repositoryUrl` in the response is a local path under `Git:LocalRepositoryRoot`.

## How To Test

Run all tests:

```powershell
dotnet test
```

Run without integration tests:

```powershell
dotnet test --filter "Category!=Integration"
```

Run only integration tests:

```powershell
dotnet test --filter "Category=Integration"
```

Integration tests are marked with `Category=Integration`. Terraform integration coverage returns early when required CLIs are not available.

Build only:

```powershell
dotnet build
```

## Validation And Guardrails

Validation happens outside the LLM. The app rejects generated Terraform when deterministic or scanner-based checks find:

- Public S3 bucket ACLs or policies.
- Missing S3 public access block resources.
- Missing S3 server-side encryption.
- Security group ingress from `0.0.0.0/0` or `::/0`.
- IAM wildcard permissions.

The full validation sequence is:

1. `terraform fmt -recursive -no-color`
2. `terraform init -backend=false -input=false -no-color`
3. `terraform validate -no-color`
4. `tflint --no-color`
5. Deterministic security policy plus `tfsec --format json --no-color .`
6. `terraform plan -refresh=false -input=false -no-color`

Missing AWS credentials during `terraform plan` are treated as acceptable for local validation, as long as the earlier structural and security checks pass.

## File Reference

### Root

- `agents.md` - Phase 1 scope, validation requirements, retry limit, and local demo behavior.
- `InfraAgent.sln` - solution file.
- `README.md` - developer and operator documentation.
- `api-run-local.stdout.log`, `api-run-local.stderr.log`, `api-run.stdout.log`, `api-run.stderr.log` - prior local run output.

### `InfraAgent.Api`

- `InfraAgent.Api.csproj` - ASP.NET Core web project with references to Core and Tools and Swagger dependencies.
- `Program.cs` - application entrypoint. Registers options, services, generator/publisher selection, Swagger, `GET /`, and `POST /generate`.
- `appsettings.json` - default application configuration.
- `appsettings.Development.json` - development overrides.
- `Properties/launchSettings.json` - local run profiles, ports, and environment defaults.
- `generated-work/` - timestamped validation workspaces created by local API runs.
- `generated-repositories/` - locally committed generated Terraform repositories when GitHub publishing is disabled.

### `InfraAgent.Core/Context`

- `ContextDocument.cs` - record containing a context file name and content.
- `IContextRetriever.cs` - abstraction for loading generation context.
- `FileContextRetriever.cs` - loads `aws_s3.md` and/or `aws_ec2.md` based on parsed intent.
- `aws_s3.md` - S3-specific generation guidance.
- `aws_ec2.md` - EC2-specific generation guidance.

### `InfraAgent.Core/Generation`

- `ITerraformGenerator.cs` - generator interface.
- `GeneratedTerraform.cs` - generated file map, summary, and assumptions.
- `TemplateTerraformGenerator.cs` - deterministic local generator used when no OpenAI key is configured.
- `OpenAiTerraformGenerator.cs` - OpenAI-backed generator that emits Terraform through a tool-call schema.

### `InfraAgent.Core/Intent`

- `IIntentParser.cs` - prompt-to-intent parser interface.
- `IntentParser.cs` - deterministic parser and safety gate for supported resource requests.
- `IntentParseResult.cs` - parsed intent or clarifying question.
- `InfrastructureIntent.cs` - full parsed request, selected resources, region, and assumptions.
- `ResourceKind.cs` - supported resource kind enum.
- `S3BucketIntent.cs` - S3 bucket intent settings.
- `Ec2InstanceIntent.cs` - EC2 intent settings.
- `IngressRuleIntent.cs` - security group ingress rule intent.

### `InfraAgent.Core/Options`

- `AgentOptions.cs` - region, allowed instance types, retry count, and working folder options.
- `OpenAiOptions.cs` - OpenAI API key and model options.

### `InfraAgent.Core/Orchestration`

- `IInfrastructureAgent.cs` - orchestration interface.
- `InfrastructureAgent.cs` - main generation, validation, retry, README writing, and publishing workflow.
- `GenerateRequest.cs` - API request body.
- `GenerateResponse.cs` - API response body and factory helpers.

### `InfraAgent.Core/Validation`

- `IInfrastructureValidator.cs` - validator interface.
- `InfrastructureValidator.cs` - runs Terraform, tflint, security scanning, and plan checks.
- `ValidationResult.cs` - validation success/failure record.

### `InfraAgent.Tools/Git`

- `IGitRepository.cs` - publishing abstraction.
- `RepositoryPublishRequest.cs` - publish input.
- `RepositoryPublishResult.cs` - publish output.
- `GitOptions.cs` - local/GitHub publishing options.
- `LocalGitRepository.cs` - copies generated files, creates a local Git repository, stages, and commits.
- `GitHubRepository.cs` - creates a GitHub repository, commits generated files, and pushes to `main`.

### `InfraAgent.Tools/Processes`

- `IProcessRunner.cs` - external process abstraction.
- `ProcessRunner.cs` - executes CLI tools with captured stdout/stderr.
- `CommandResult.cs` - command name, arguments, exit code, and output.

### `InfraAgent.Tools/Security`

- `ISecurityScanner.cs` - security scanner abstraction.
- `TfsecSecurityScanner.cs` - combines deterministic policy checks with parsed `tfsec` JSON findings.
- `DeterministicSecurityPolicy.cs` - regex-based guardrails for S3, security groups, and IAM.
- `SecurityFinding.cs` - individual security finding.
- `SecurityScanResult.cs` - collection of findings and formatted error text.

### `InfraAgent.Tools/Terraform`

- `ITerraformRunner.cs` - Terraform CLI abstraction.
- `TerraformRunner.cs` - wraps `terraform fmt`, `init`, `validate`, and `plan`.
- `ITflintRunner.cs` - tflint abstraction.
- `TflintRunner.cs` - wraps `tflint --no-color`.

### `InfraAgent.Tests`

- `InfraAgent.Tests.csproj` - xUnit test project.
- `Core/IntentParserTests.cs` - parser and clarification behavior.
- `Core/TemplateTerraformGeneratorTests.cs` - deterministic Terraform generation behavior.
- `Core/InfrastructureAgentTests.cs` - orchestration, retry, and publishing behavior.
- `Core/DeterministicSecurityPolicyTests.cs` - deterministic guardrail coverage.
- `Integration/TerraformRunnerIntegrationTests.cs` - Terraform CLI integration smoke tests.
- `Fakes/FakeGitRepository.cs` - in-memory publishing test double.
- `Fakes/SequenceTerraformGenerator.cs` - generator test double for retry scenarios.
- `Fakes/SequenceValidator.cs` - validator test double for retry scenarios.
