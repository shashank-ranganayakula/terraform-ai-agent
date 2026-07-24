namespace InfraAgent.Core.Orchestration;

public sealed record GenerateResponse(
    string Status,
    string? ClarifyingQuestion,
    string? RepositoryUrl,
    IReadOnlyList<string> FilesCreated,
    string Summary,
    IReadOnlyList<string> Assumptions,
    string? Error,
    string? ProvisioningStatus = null,
    string? ProvisioningOutput = null)
{
    public static GenerateResponse Clarification(string question) =>
        new("clarification_required", question, null, Array.Empty<string>(), string.Empty, Array.Empty<string>(), null);

    public static GenerateResponse Success(
        string repositoryUrl,
        IReadOnlyList<string> files,
        string summary,
        IReadOnlyList<string> assumptions,
        string provisioningOutput) =>
        new("succeeded", null, repositoryUrl, files, summary, assumptions, null, "applied", provisioningOutput);

    public static GenerateResponse Failure(string error) =>
        new("failed", null, null, Array.Empty<string>(), string.Empty, Array.Empty<string>(), error);

    public static GenerateResponse PreflightFailure(string error, string output) =>
        new("failed", null, null, Array.Empty<string>(), string.Empty, Array.Empty<string>(), error, "not_started", output);

    public static GenerateResponse PublishFailure(
        string error,
        string output,
        string summary,
        IReadOnlyList<string> assumptions) =>
        new("failed", null, null, Array.Empty<string>(), summary, assumptions, error, "not_started", output);

    public static GenerateResponse ProvisioningFailure(
        string error,
        string output,
        string? repositoryUrl = null,
        IReadOnlyList<string>? files = null,
        string summary = "",
        IReadOnlyList<string>? assumptions = null) =>
        new("failed", null, repositoryUrl, files ?? Array.Empty<string>(), summary, assumptions ?? Array.Empty<string>(), error, "failed", output);
}
