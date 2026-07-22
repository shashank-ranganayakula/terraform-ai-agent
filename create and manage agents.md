# Create and Manage Agents

This file is the source of truth for Phase 1.

## Phase 1 Scope

- Build a real .NET 8 solution with `InfraAgent.Api`, `InfraAgent.Core`, `InfraAgent.Tools`, and `InfraAgent.Tests`.
- Support only AWS S3 buckets and EC2 instances.
- S3 must include versioning, server-side encryption, and public access block resources.
- EC2 must include an AMI lookup, an instance, and an associated security group.
- Do not generate multi-cloud, VPC-from-scratch, RDS, Lambda, remote state backends, or CI/CD pipelines.

## Guardrails

- Do not rely on the LLM to police security.
- Fail deterministic validation for public S3 buckets, unencrypted S3 buckets, security group ingress from `0.0.0.0/0` or `::/0`, and IAM wildcard policies.
- Run Terraform CLI validation and `tfsec`.
- Retry generation at most three times.

## AWS Provisioning

- After Terraform is generated and validation passes, the backend publishes the generated Terraform to GitHub, then automatically applies it for the AWS credentials available to the API process.
- The backend must never run `terraform destroy`.
- The command flow is intentionally minimal: `terraform fmt`, `terraform init -backend=false`, `terraform validate`, `tflint`, `tfsec`, then `terraform apply -input=false -auto-approve -no-color`.
- Terraform state files must remain out of the published generated repository.
- AWS credentials and region must come from the normal AWS environment/profile used by the API process; generated Terraform must not contain hardcoded AWS credentials.

## Local Demo Mode

When no OpenAI API key is configured, the API uses a deterministic generator.
When no GitHub token is configured, the API writes and commits a local Git repository under the configured local repository root.
Development settings must not override a configured `Git:GitHubToken` or `Git:GitHubOwner` with blanks.

## GitHub Publishing

- When `Git:GitHubToken` is configured, the backend must create a GitHub repository and push the generated Terraform files to it.
- `Git:GitHubOwner` selects the user or organization that receives the repository.
- If `Git:GitHubOwner` is blank or matches the authenticated token user, create a personal repository with the user repository endpoint.
- If `Git:GitHubOwner` is different from the authenticated token user, treat it as an organization owner and require organization repository permissions.
- Local repository publishing is only a fallback when no GitHub token is configured.
- Publishing happens before `terraform apply` so the generated code is available in GitHub even if AWS provisioning fails.
- Terraform state, `.terraform`, and `.git` folders must not be included in the pushed repository file list.
- `.terraform.lock.hcl` should be committed when Terraform creates it.

## Frontend Integration

- The Angular frontend lives in `src` and is run from the solution root with `npm.cmd run start`.
- Angular calls the backend at `http://localhost:5123/generate` in local development so the app works even when started with `ng serve` directly.
- `proxy.conf.json` is kept as an optional dev proxy for `npm.cmd run start`, but the frontend does not depend on it.
- Generated repository static files are served by `InfraAgent.Api` from `InfraAgent.Api/generated-repositories` at `/generated-repositories`.
- The frontend expects the backend `GenerateResponse` contract: `status`, `clarifyingQuestion`, `repositoryUrl`, `filesCreated`, `summary`, `assumptions`, and `error`.
- Provisioning responses also include `provisioningStatus` and `provisioningOutput`.
- The UI must give generation requests 2-3 minutes before treating the API as unresponsive because Terraform generation and validation can be slow.
