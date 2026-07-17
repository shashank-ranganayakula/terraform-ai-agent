using InfraAgent.Core.Validation;

namespace InfraAgent.Tests.Fakes;

public sealed class SequenceValidator(params ValidationResult[] results) : IInfrastructureValidator
{
    private int _index;

    public int Calls => _index;

    public Task<ValidationResult> ValidateAsync(string workingDirectory, CancellationToken cancellationToken)
    {
        var next = results[Math.Min(_index, results.Length - 1)];
        _index++;
        return Task.FromResult(next);
    }
}
