using InfraAgent.Core.Validation;

namespace InfraAgent.Tests.Fakes;

public sealed class SequenceValidator(params ValidationResult[] results) : IInfrastructureValidator
{
    private int _index;
    private readonly List<string> _workingDirectories = [];

    public int Calls => _index;

    public IReadOnlyList<string> WorkingDirectories => _workingDirectories;

    public Task<ValidationResult> ValidateAsync(string workingDirectory, CancellationToken cancellationToken)
    {
        _workingDirectories.Add(workingDirectory);
        var next = results[Math.Min(_index, results.Length - 1)];
        _index++;
        return Task.FromResult(next);
    }
}
