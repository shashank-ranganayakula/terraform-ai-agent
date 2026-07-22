using System.Text;
using System.Text.RegularExpressions;

namespace InfraAgent.Core.Generation;

public static class TerraformVariablePruner
{
    private static readonly Regex VariableDeclarationPattern = new(
        @"(?m)^[ \t]*variable\s+""(?<name>[A-Za-z0-9_]+)""\s*\{",
        RegexOptions.Compiled);

    private static readonly Regex VariableReferencePattern = new(
        @"\bvar\.(?<name>[A-Za-z0-9_]+)\b",
        RegexOptions.Compiled);

    private static readonly Regex TfvarsAssignmentPattern = new(
        @"(?m)^[ \t]*(?<name>[A-Za-z0-9_]+)\s*=",
        RegexOptions.Compiled);

    public static GeneratedTerraform PruneUnusedVariables(GeneratedTerraform terraform)
    {
        if (!terraform.Files.TryGetValue("variables.tf", out var variables))
        {
            return terraform;
        }

        var declaredVariables = VariableDeclarationPattern
            .Matches(variables)
            .Select(match => match.Groups["name"].Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (declaredVariables.Count == 0)
        {
            return terraform;
        }

        var referencedVariables = terraform.Files
            .Where(file => !IsVariablesFile(file.Key) && !IsTfvarsFile(file.Key))
            .SelectMany(file => VariableReferencePattern
                .Matches(file.Value)
                .Select(match => match.Groups["name"].Value))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var unusedVariables = declaredVariables
            .Where(variable => !referencedVariables.Contains(variable))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (unusedVariables.Count == 0)
        {
            return terraform;
        }

        var files = terraform.Files.ToDictionary(
            file => file.Key,
            file => file.Value,
            StringComparer.OrdinalIgnoreCase);

        files["variables.tf"] = RemoveVariableBlocks(variables, unusedVariables);

        foreach (var tfvarsFile in files.Keys.Where(IsTfvarsFile).ToArray())
        {
            files[tfvarsFile] = RemoveTfvarsAssignments(files[tfvarsFile], unusedVariables);
        }

        var assumptions = terraform.Assumptions
            .Concat([$"Removed unused Terraform variables: {string.Join(", ", unusedVariables.Order(StringComparer.OrdinalIgnoreCase))}."])
            .ToArray();

        return new GeneratedTerraform(files, terraform.Summary, assumptions);
    }

    private static bool IsVariablesFile(string path) =>
        path.Replace('\\', '/').Equals("variables.tf", StringComparison.OrdinalIgnoreCase);

    private static bool IsTfvarsFile(string path) =>
        path.EndsWith(".tfvars", StringComparison.OrdinalIgnoreCase);

    private static string RemoveVariableBlocks(string content, IReadOnlySet<string> unusedVariables)
    {
        var result = new StringBuilder();
        var cursor = 0;

        foreach (Match match in VariableDeclarationPattern.Matches(content))
        {
            var variableName = match.Groups["name"].Value;
            if (!unusedVariables.Contains(variableName))
            {
                continue;
            }

            var blockEnd = FindBlockEnd(content, match.Index);
            result.Append(content, cursor, match.Index - cursor);
            cursor = blockEnd;

            while (cursor < content.Length && (content[cursor] == '\r' || content[cursor] == '\n'))
            {
                cursor++;
            }
        }

        result.Append(content, cursor, content.Length - cursor);
        return result.ToString().TrimEnd() + Environment.NewLine;
    }

    private static int FindBlockEnd(string content, int start)
    {
        var openBrace = content.IndexOf('{', start);
        if (openBrace < 0)
        {
            return content.Length;
        }

        var depth = 0;
        for (var index = openBrace; index < content.Length; index++)
        {
            if (content[index] == '{')
            {
                depth++;
            }
            else if (content[index] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return index + 1;
                }
            }
        }

        return content.Length;
    }

    private static string RemoveTfvarsAssignments(string content, IReadOnlySet<string> unusedVariables)
    {
        var lines = content
            .Split(["\r\n", "\n"], StringSplitOptions.None)
            .Where(line =>
            {
                var match = TfvarsAssignmentPattern.Match(line);
                return !match.Success || !unusedVariables.Contains(match.Groups["name"].Value);
            });

        return string.Join(Environment.NewLine, lines).TrimEnd() + Environment.NewLine;
    }
}
