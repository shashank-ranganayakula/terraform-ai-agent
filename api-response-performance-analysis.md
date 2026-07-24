# API Response Performance And Generated Artifact Analysis

This document explains why `POST /generate` can be slow, what `generated-work` and `generated-repositories` are for, why generation may run three times, why the folders have very different sizes, and the main bottlenecks in the current backend.

## Request Flow

The request enters `InfraAgent.Api/Program.cs` at `POST /generate` and calls `InfrastructureAgent.GenerateAsync`.

Current end-to-end flow:

1. Parse the user prompt into a supported infrastructure intent.
2. Retrieve RAG/context documents.
3. Ask the OpenAI-compatible API to generate Terraform.
4. Prune unused Terraform variables.
5. Create one timestamped request folder under `generated-work`.
6. Write generated Terraform files to that folder.
7. Validate the folder with Terraform and security tools.
8. Retry generation in the same request folder if validation fails, up to `Agent:MaxRepairAttempts`.
9. Write README and `.gitignore`.
10. Publish the generated files to GitHub or local fallback.
11. Run `terraform apply`.
12. Return one response to the frontend.

Because all of this happens inside one HTTP request, the frontend does not receive a final response until generation, validation, publishing, and AWS apply have all finished.

## Why The API Response Is Slow

The API is slow mainly because the endpoint performs multiple heavy operations synchronously.

Likely slow steps:

- Model call: `OpenAiTerraformGenerator.GenerateAsync` calls `CompleteChatAsync`, which depends on the external API latency.
- Terraform init: `terraform init -backend=false -input=false -no-color` still installs the AWS provider into the request working directory when the provider is not already available from cache.
- Provider size: in this workspace, a recent `.terraform` folder contains `terraform-provider-aws_v6.55.0_x5.exe` at about `846.65 MB`.
- Validation tools: every successful attempt runs `terraform fmt`, `terraform init`, `terraform validate`, `tflint`, and `tfsec`.
- Retries: if validation fails, the entire model generation plus validation cycle is repeated.
- GitHub publishing: repository creation, local git init/commit, and push are network and disk operations.
- AWS provisioning: `terraform apply -input=false -auto-approve -no-color` calls AWS APIs and waits for resources to be created.

There is no background job, polling endpoint, queue, streaming status, or early return. The browser waits for the full workflow.

## Why It Runs Three Times

The retry count comes from `AgentOptions.MaxRepairAttempts`, which defaults to `3`.

`InfrastructureAgent.GenerateAsync` has this loop:

```text
for attempt = 1 to MaxRepairAttempts
  generate Terraform
  clear generated source files in the request generated-work folder
  preserve .terraform and .terraform.lock.hcl
  validate Terraform
  stop if validation succeeds
  otherwise send validation output back as repair instructions
```

So it only runs three times when validation fails. For example, if `tflint` reports an unused variable, the backend treats that as a failed attempt and asks the generator to repair the Terraform. Retries now reuse the same timestamped `generated-work` folder so Terraform can reuse initialized provider files during the same request.

If validation succeeds on attempt 1, it should not run attempts 2 or 3.

## What `generated-work` Is

`generated-work` is the backend staging and execution directory.

It is used for:

- Writing raw/generated Terraform files.
- Preserving `.terraform` and `.terraform.lock.hcl` across repair attempts in the same request.
- Running `terraform fmt`.
- Running `terraform init`.
- Running `terraform validate`.
- Running `tflint`.
- Running `tfsec`.
- Initializing a local git repository when publishing to GitHub.
- Running `terraform apply`.
- Holding local Terraform state after apply.

Important: after `terraform apply`, `generated-work` is no longer just temporary scratch. Because this project uses `terraform init -backend=false` and does not configure remote state, the local `terraform.tfstate` created in `generated-work` is the state file Terraform would need later to understand what it created.

## What `generated-repositories` Is

`generated-repositories` is the local publishing fallback.

When no GitHub token is configured, `LocalGitRepository` copies the final generated Terraform files from `generated-work` into `generated-repositories/<repo-name>`, initializes git there, and commits the files locally.

When GitHub publishing is configured, `GitHubRepository` does not depend on `generated-repositories`. It initializes git directly in the final `generated-work` folder, creates a GitHub repo, commits, and pushes.

The API still serves `InfraAgent.Api/generated-repositories` as static files for local fallback/debug history.

## Why `generated-work` Is Much Larger

In this workspace, recent folder sizes show:

- `generated-work/20260722152722868`: about `846.68 MB`
- `generated-repositories/infra-agent-20260721172249`: about `0.01 MB`

The size difference is almost entirely Terraform provider installation.

The latest measured `generated-work` folder contains:

- `.terraform/`: about `846.67 MB`
- AWS provider executable: about `846.65 MB`
- Terraform source files, README, lock file, and state: tiny by comparison

`generated-repositories` is small because publishing excludes:

- `.terraform/`
- `.git/` from file list handling
- `terraform.tfstate`
- `terraform.tfstate.backup`
- `*.tfstate.*`

It keeps the source files and should keep `.terraform.lock.hcl`.

## Does `generated-repositories` Depend On `generated-work`?

During a request, yes: the generated repository is created from the final successful `generated-work` directory.

After publishing:

- Local fallback repositories do not need `generated-work` just to view the copied Terraform files.
- GitHub repositories do not need `generated-work` just to view the pushed Terraform files.
- AWS lifecycle management does depend on Terraform state, and today that state lives in `generated-work`.

So the source code in GitHub/local repo does not depend on `generated-work`, but the local Terraform state currently does.

## Overall Bottlenecks

The biggest bottlenecks are:

- Terraform provider install: each new request folder still needs Terraform initialization, but provider downloads are now cached through `TF_PLUGIN_CACHE_DIR`.
- Retry amplification: one lint or validation failure can multiply model calls and Terraform init work by up to three.
- Synchronous API design: the request blocks until generation, validation, GitHub publishing, and AWS apply finish.
- AWS apply time: creating S3/EC2 resources is naturally slower than generating files.
- External service latency: model API, GitHub API, Terraform registry, and AWS are all in the critical path.
- Local state design: state remains in `generated-work`, forcing retention of large working directories unless provider caching or remote state is introduced.
- Process startup overhead: every Terraform, tflint, and tfsec command is a separate child process.
- No cleanup/retention policy: successful applied working directories accumulate because they contain local Terraform state.

## Recommendations

Highest-impact changes:

- Configure `TF_PLUGIN_CACHE_DIR` so Terraform reuses downloaded providers across attempts.
- Reuse a single attempt directory during repair, or clean failed attempt directories after the request.
- Add remote state if the generated GitHub repository should be the durable source for future Terraform operations.
- Split `/generate` into a background job: return a job id quickly, then expose status/logs/result endpoints.
- Publish to GitHub immediately after validation, but run apply as a separate step or job.
- Add command timing logs around model generation, Terraform init, validate, tflint, tfsec, GitHub publish, and apply.
- Add a retention policy for `generated-work` that preserves state but removes `.terraform` provider directories when safe.
- Consider disabling `tflint` fixable warnings as hard blockers only if the codebase also has deterministic cleanup for those warnings.

Current quick win:

- Provider caching is the fastest way to reduce repeated `terraform init` cost. The current measured AWS provider is roughly `846 MB`, and it is copied/installed into each successful validation attempt directory.

## Implemented Response-Time Fixes

- Terraform commands now run with `TF_PLUGIN_CACHE_DIR`. If the environment variable is not already set, the backend creates and uses `terraform-plugin-cache` under the API process working directory.
- Repair attempts now reuse the same timestamped `generated-work` directory. Before each retry, generated source files are cleared while `.terraform` and `.terraform.lock.hcl` are preserved, so a retry can reuse the already initialized provider.
- Generated Terraform is now cleaned before validation: undeclared `.tfvars` assignments are removed, and missing S3 encryption/public-access-block resources are added deterministically. This avoids common second and third attempts for fixable `tags` warnings and `S3_UNENCRYPTED` findings.
- `tflint` and `tfsec` now run concurrently after `terraform validate` succeeds, reducing validation wall-clock time without removing either check.
- The backend now logs elapsed time for context retrieval, each generation attempt, each validation attempt, repository publishing, Terraform provisioning, and the full request.

Expected impact:

- First request after an empty cache still has to fetch the provider.
- Later requests and retries should avoid the slow provider download path.
- Retries after a lint/security failure should be noticeably faster because the same initialized working directory is reused.
- AWS `terraform apply`, GitHub publishing, and the model call are still on the synchronous request path, so a request that creates real AWS resources can still take minutes.
