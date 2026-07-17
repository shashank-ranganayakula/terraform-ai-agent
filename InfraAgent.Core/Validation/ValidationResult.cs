namespace InfraAgent.Core.Validation;

public sealed record ValidationResult(bool Succeeded, string Output)
{
    public static ValidationResult Success(string output) => new(true, output);

    public static ValidationResult Failure(string output) => new(false, output);
}
