# API Error Handling

InfraAgent uses a global ASP.NET Core exception handler in `InfraAgent.Api/Program.cs`.

## Purpose

The handler prevents unhandled exceptions from being returned to the frontend as raw stack traces. It logs the detailed exception server-side and returns a stable `GenerateResponse` JSON object to the client.

## Response Shape

Unhandled exceptions return HTTP `500` with:

```json
{
  "status": "failed",
  "clarifyingQuestion": null,
  "repositoryUrl": null,
  "filesCreated": [],
  "summary": "",
  "assumptions": [],
  "error": "Unexpected API error. The request was not completed. Check backend logs for the detailed exception.",
  "provisioningStatus": null,
  "provisioningOutput": null
}
```

## Logging

The handler logs through `InfraAgent.Api.GlobalExceptionHandler` with:

- HTTP method
- request path
- full exception details in backend logs

Secret values must not be added to the user-facing error message.

## Expected Non-500 Errors

Normal validation and clarification responses still use the existing `GenerateResponse` flow:

- Clarification required: HTTP `200`, `status = "clarification_required"`.
- Generation, validation, publishing, or provisioning failure handled by the workflow: HTTP `422`, `status = "failed"`.
- Unexpected unhandled exception: HTTP `500`, `status = "failed"`.

## Current Limitations

- No correlation/request ID is returned.
- No centralized telemetry sink is configured.
- The frontend displays the `error` field but does not distinguish all failure categories visually.
