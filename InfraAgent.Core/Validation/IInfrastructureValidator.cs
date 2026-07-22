namespace InfraAgent.Core.Validation;

public interface IInfrastructureValidator
{
    Task<ValidationResult> ValidateAsync(string workingDirectory, CancellationToken cancellationToken);
}
