namespace InfraAgent.Core.Orchestration;

public sealed record GenerateResponse(
    string Status,
    string? ClarifyingQuestion,
    string? RepositoryUrl,
    IReadOnlyList<string> FilesCreated,
    string Summary,
    IReadOnlyList<string> Assumptions,
    string? Error)
{
    public static GenerateResponse Clarification(string question) =>
        new("clarification_required", question, null, Array.Empty<string>(), string.Empty, Array.Empty<string>(), null);

    public static GenerateResponse Success(string repositoryUrl, IReadOnlyList<string> files, string summary, IReadOnlyList<string> assumptions) =>
        new("succeeded", null, repositoryUrl, files, summary, assumptions, null);

    public static GenerateResponse Failure(string error) =>
        new("failed", null, null, Array.Empty<string>(), string.Empty, Array.Empty<string>(), error);
}
