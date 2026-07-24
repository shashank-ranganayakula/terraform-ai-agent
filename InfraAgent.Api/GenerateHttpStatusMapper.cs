using InfraAgent.Core.Orchestration;

public static class GenerateHttpStatusMapper
{
    public static int ForResponse(GenerateResponse response)
    {
        if (response.Status == "succeeded")
        {
            return StatusCodes.Status200OK;
        }

        if (response.Status == "clarification_required")
        {
            return StatusCodes.Status400BadRequest;
        }

        var details = BuildDetails(response.Error, response.ProvisioningOutput, response.Summary);

        if (ContainsAny(details, "rate limit", "too many requests", " 429", "(429)"))
        {
            return StatusCodes.Status429TooManyRequests;
        }

        if (ContainsAny(details, "timeout", "timed out", "deadline exceeded", "operation canceled", "task was canceled"))
        {
            return StatusCodes.Status504GatewayTimeout;
        }

        if (ContainsAny(details, "bucketalreadyexists", "bucketalreadynotownedbyyou", "already exists", "name conflict", "conflict", "(409)"))
        {
            return StatusCodes.Status409Conflict;
        }

        if (ContainsAny(details, "accessdenied", "forbidden", "permission", "not authorized", "unauthorizedexception", "(403)"))
        {
            return StatusCodes.Status403Forbidden;
        }

        if (ContainsAny(details, "authentication", "api key is missing", "token is required", "invalidclienttokenid", "expiredtoken", "nocredentialproviders", "no valid credential"))
        {
            return StatusCodes.Status401Unauthorized;
        }

        if (ContainsAny(details, "not found", "(404)", "statuscode: notfound"))
        {
            return StatusCodes.Status404NotFound;
        }

        if (ContainsAny(details, "terraform is required", "tflint is required", "tfsec is required", "must be available on path"))
        {
            return StatusCodes.Status503ServiceUnavailable;
        }

        if (ContainsAny(details, "github", "aws", "openai", "ai provider", "provider", "terraform apply failed"))
        {
            return StatusCodes.Status502BadGateway;
        }

        return StatusCodes.Status422UnprocessableEntity;
    }

    public static int ForException(Exception exception)
    {
        var details = BuildDetails(exception.ToString());

        if (exception is TimeoutException or OperationCanceledException ||
            ContainsAny(details, "timeout", "timed out", "deadline exceeded", "task was canceled"))
        {
            return StatusCodes.Status504GatewayTimeout;
        }

        if (ContainsAny(details, "rate limit", "too many requests", " 429", "(429)"))
        {
            return StatusCodes.Status429TooManyRequests;
        }

        if (ContainsAny(details, "accessdenied", "forbidden", "permission", "not authorized", "(403)"))
        {
            return StatusCodes.Status403Forbidden;
        }

        if (ContainsAny(details, "authentication", "api key is missing", "token is required", "invalidclienttokenid", "expiredtoken", "nocredentialproviders", "no valid credential"))
        {
            return StatusCodes.Status401Unauthorized;
        }

        if (ContainsAny(details, "not found", "(404)", "statuscode: notfound"))
        {
            return StatusCodes.Status404NotFound;
        }

        if (ContainsAny(details, "openai", "github", "aws", "octokit", "apiresponseexception", "httprequestexception"))
        {
            return StatusCodes.Status502BadGateway;
        }

        if (ContainsAny(details, "baseurl is required", "service unavailable", "terraform is required", "must be available on path"))
        {
            return StatusCodes.Status503ServiceUnavailable;
        }

        return StatusCodes.Status500InternalServerError;
    }

    private static string BuildDetails(params string?[] values) =>
        string.Join('\n', values.Where(value => !string.IsNullOrWhiteSpace(value))).ToLowerInvariant();

    private static bool ContainsAny(string value, params string[] patterns) =>
        patterns.Any(pattern => value.Contains(pattern, StringComparison.OrdinalIgnoreCase));
}
