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

## Local Demo Mode

When no OpenAI API key is configured, the API uses a deterministic generator.
When no GitHub token is configured, the API writes and commits a local Git repository under the configured local repository root.
