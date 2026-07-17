namespace InfraAgent.Core.Generation;

public sealed record GeneratedTerraform(IReadOnlyDictionary<string, string> Files, string Summary, IReadOnlyList<string> Assumptions);
