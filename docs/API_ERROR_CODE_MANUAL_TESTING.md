# API Error Code Manual Testing

This guide explains how to manually check the backend status codes and the matching frontend error explanations for InfraAgent.

## Setup

Run the backend:

```powershell
dotnet run --project InfraAgent.Api\InfraAgent.Api.csproj --launch-profile https
```

Run the frontend:

```powershell
npm.cmd start
```

The Angular app calls `https://localhost:7112/generate`. For direct backend testing, use `curl.exe -k` to ignore the local development certificate:

```powershell
$Api = "https://localhost:7112"
curl.exe -k -i -H "Content-Type: application/json" -d "{\"prompt\":\"\"}" "$Api/generate"
```

All `/generate` responses use the same JSON shape:

```json
{
  "status": "failed",
  "clarifyingQuestion": null,
  "repositoryUrl": null,
  "filesCreated": [],
  "summary": "",
  "assumptions": [],
  "error": "Readable error text",
  "provisioningStatus": null,
  "provisioningOutput": null
}
```

For frontend checks, submit the prompt in the chat UI and confirm the assistant card shows:

- a short explanation title
- an `HTTP <code>` chip when the backend returned an error status
- next steps
- raw Terraform logs when the backend supplied `provisioningOutput`

## Status Codes

| Code | Applicable | How to test | Expected backend response | Expected frontend display |
|---|---:|---|---|---|
| `200` | Yes | Submit a valid supported prompt, for example `Create an encrypted S3 bucket named <globally-unique-name> in ap-south-2`. This creates AWS infrastructure. | HTTP `200`; `status: "succeeded"`; repository URL, files, summary, assumptions, `provisioningStatus: "applied"`, Terraform output. | Normal success card with generated GitHub repository link and Terraform Apply summary. |
| `201` | No | Not used by this project. `/generate` returns an operation result, not a REST-created API resource. | Not expected. | Not expected. |
| `202` | No | Not used by this project. Deployment is synchronous, not a background job. | Not expected. | Not expected. |
| `400` | Yes | Send an empty prompt: `curl.exe -k -i -H "Content-Type: application/json" -d "{\"prompt\":\"\"}" "$Api/generate"` | HTTP `400`; `status: "clarification_required"`; `clarifyingQuestion` explains what is missing. | Error card: `Invalid request or prompt`, `HTTP 400`, next steps to include service, resource name, and region. |
| `400` | Yes | Submit `Create an S3 bucket` without a region, or include unsupported/destructive content such as `destroy my bucket`. | HTTP `400`; `status: "clarification_required"`; `clarifyingQuestion` explains the guardrail. | Same `HTTP 400` card with prompt correction guidance. |
| `401` | Yes | Temporarily remove or invalidate `OpenAI:ApiKey`, then restart the backend and submit any valid prompt. Do not commit the config change. | HTTP `401`; `status: "failed"`; `error` mentions missing/rejected authentication. | Error card: `Authentication required`, `HTTP 401`, next steps to check API keys, GitHub token, and AWS credentials. |
| `403` | Yes | Use AWS credentials that authenticate but do not have permission to create S3/EC2, then submit a valid prompt. | HTTP `403`; `status: "failed"`; usually `provisioningStatus: "failed"` with AWS permission logs. | Error card: `Permission denied`, `HTTP 403`, with raw Terraform logs available. |
| `404` | Yes | Backend route test: call a missing route, for example `curl.exe -k -i "$Api/not-a-route"`. Frontend route test: temporarily point `apiUrl` to a backend path/host that does not expose `/generate`. | Backend missing route returns HTTP `404`. If `/generate` receives a mapped upstream not-found error, it returns `GenerateResponse` with HTTP `404`. | Error card: `Resource not found`, `HTTP 404`. |
| `409` | Yes | Submit an S3 prompt with a bucket name that already exists globally or already belongs to another account. | HTTP `409`; `status: "failed"`; Terraform/AWS output includes bucket conflict such as `BucketAlreadyExists`. | Error card: `Resource conflict`, `HTTP 409`, with next steps to choose a unique bucket or repository name. |
| `422` | Yes | Force Terraform validation failure after all repair attempts, for example by temporarily making a validator tool fail or by using generated Terraform that cannot validate. | HTTP `422`; `status: "failed"`; `error` or `provisioningOutput` contains validation/lint/security details. | Error card: `Terraform could not be processed`, `HTTP 422`, raw logs shown when available. |
| `429` | Yes | Use an AI provider/API key that is currently rate limited, or repeatedly submit until the upstream provider returns rate limit text. | HTTP `429`; `status: "failed"`; `error` indicates rate limit/throttling. | Error card: `Rate limit exceeded`, `HTTP 429`, next steps to wait and retry. |
| `500` | Yes | Trigger an unexpected backend exception that is not classified as auth, permission, timeout, upstream, or service unavailable. Use logs to confirm. | HTTP `500`; `status: "failed"`; generic unexpected API error. | Error card: `Internal server error`, `HTTP 500`, next steps to inspect backend logs. |
| `502` | Yes | Break an upstream dependency without making it an auth issue, for example set the AI base URL to an unavailable/wrong upstream or cause GitHub/AWS provider failure. | HTTP `502`; `status: "failed"`; `error` says request failed or upstream/provider operation failed. | Error card: `Upstream service failure`, `HTTP 502`, with raw logs if supplied. |
| `503` | Yes | Remove Terraform from `PATH` or run the backend on a machine without required CLI tooling, then submit a valid prompt. | HTTP `503`; `status: "failed"`; output says Terraform or a required tool must be available on `PATH`. | Error card: `Service unavailable`, `HTTP 503`, next steps to install tooling and restart the API. |
| `504` | Yes | Force a timeout/cancellation, for example by making the upstream AI endpoint hang until the request is canceled. | HTTP `504`; `status: "failed"`; error indicates timeout/cancellation. | Error card: `Deployment timeout`, `HTTP 504`, next steps to wait, check AWS/GitHub, and retry. |

## Quick Backend Commands

Health check:

```powershell
curl.exe -k -i "$Api/"
```

Invalid prompt:

```powershell
curl.exe -k -i -H "Content-Type: application/json" -d "{\"prompt\":\"\"}" "$Api/generate"
```

Missing region:

```powershell
curl.exe -k -i -H "Content-Type: application/json" -d "{\"prompt\":\"Create an encrypted S3 bucket named my-test-bucket\"}" "$Api/generate"
```

Valid request, creates infrastructure:

```powershell
curl.exe -k -i -H "Content-Type: application/json" -d "{\"prompt\":\"Create an encrypted S3 bucket named <globally-unique-name> in ap-south-2\"}" "$Api/generate"
```

Missing route:

```powershell
curl.exe -k -i "$Api/not-a-route"
```

## Notes

- Do not leave intentionally broken credentials or configuration in `appsettings.json`.
- Some status codes depend on real upstream behavior from AWS, GitHub, Terraform, or the AI provider. If the upstream returns different wording, the status may classify as `422`, `500`, or `502`; check backend logs to confirm.
- `201` and `202` are intentionally not implemented for the current synchronous `/generate` endpoint.
