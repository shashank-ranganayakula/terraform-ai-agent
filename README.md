# InfraAgent Phase 1

InfraAgent is a .NET 8 infrastructure-provisioning agent. It accepts a natural-language prompt, fills a strongly typed intent model, generates Terraform for Phase 1 AWS S3 and EC2 resources, validates the generated code, and commits it to a Git repository.

The source-of-truth scope file is [create and manage agents.md](create%20and%20manage%20agents.md).

## Projects

- `InfraAgent.Api` - ASP.NET Core Minimal API with `POST /generate`.
- `InfraAgent.Core` - intent parsing, context retrieval, Terraform generation, guardrails, validation orchestration, and repair loop.
- `InfraAgent.Tools` - mockable wrappers for Terraform CLI, `tflint`, `tfsec`, process execution, local Git, and GitHub publishing.
- `InfraAgent.Tests` - xUnit unit, structural, orchestration, and categorized integration tests.

## Requirements

- .NET 8 SDK and ASP.NET Core 8 shared runtime, or a newer SDK with the .NET 8 targeting pack and ASP.NET Core 8 runtime installed.
- `terraform`, `tflint`, and `tfsec` available on `PATH`.
- OpenAI API key for model-backed generation. Without it, local demo mode uses the deterministic generator.
- GitHub token for remote repository creation. Without it, local demo mode commits to a local repository folder.

## Configuration

Configuration lives in `InfraAgent.Api/appsettings.json` and can be overridden with environment variables.

```powershell 
$env:OpenAI__ApiKey = "sk-live-2f44385d34cf8a48d77bd95f0e067b946ebef3f1dcfbf492f956e2d478bff2cc"
$env:OpenAI__Model = "gpt-4.1-mini"
$env:Git__GitHubToken = "github_pat_..."
$env:Git__GitHubOwner = "your-org-or-user"
```

Do not put AWS credentials in generated Terraform. Use normal AWS SDK/CLI environment configuration if you intend to run Terraform against real AWS. Include the AWS region explicitly in each prompt, for example `in us-east-1`.

## Run

```powershell
dotnet restore
dotnet run --project InfraAgent.Api
```

Then call:

```powershell
Invoke-RestMethod -Method Post `
  -Uri http://localhost:5000/generate `
  -ContentType 'application/json' `
  -Body '{"prompt":"Create an S3 bucket for user uploads with versioning enabled in us-east-1, and a t3.medium EC2 instance to run a web server"}'
```

If `Git:GitHubToken` is not configured, the response repository URL is a local path under `Git:LocalRepositoryRoot`.

## Test

```powershell
dotnet test
```

Integration tests are marked with `Category=Integration` and return early when the Terraform CLI is not on `PATH`.

## Guardrails

The deterministic policy layer fails generated code that contains public S3 access, missing S3 encryption, security-group ingress from `0.0.0.0/0` or `::/0`, or IAM wildcard policies. These checks run outside the LLM and remain enforced even if a prompt asks to bypass safety rules.
