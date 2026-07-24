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
        var files = terraform.Files.ToDictionary(
            file => file.Key,
            file => file.Value,
            StringComparer.OrdinalIgnoreCase);

        var removedTfvarsAssignments = Array.Empty<string>();
        if (!terraform.Files.TryGetValue("variables.tf", out var variables))
        {
            removedTfvarsAssignments = RemoveUndeclaredTfvarsAssignments(files, []);
            return WithAssumptionIfNeeded(terraform, files, removedTfvarsAssignments);
        }

        var declaredVariables = VariableDeclarationPattern
            .Matches(variables)
            .Select(match => match.Groups["name"].Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (declaredVariables.Count == 0)
        {
            removedTfvarsAssignments = RemoveUndeclaredTfvarsAssignments(files, declaredVariables);
            return WithAssumptionIfNeeded(terraform, files, removedTfvarsAssignments);
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

        if (unusedVariables.Count != 0)
        {
            files["variables.tf"] = RemoveVariableBlocks(variables, unusedVariables);
        }

        foreach (var tfvarsFile in files.Keys.Where(IsTfvarsFile).ToArray())
        {
            files[tfvarsFile] = RemoveTfvarsAssignments(files[tfvarsFile], unusedVariables);
        }

        removedTfvarsAssignments = RemoveUndeclaredTfvarsAssignments(files, declaredVariables.Except(unusedVariables, StringComparer.OrdinalIgnoreCase));

        var assumptions = terraform.Assumptions
            .Concat(unusedVariables.Count == 0
                ? []
                : [$"Removed unused Terraform variables: {string.Join(", ", unusedVariables.Order(StringComparer.OrdinalIgnoreCase))}."])
            .Concat(removedTfvarsAssignments.Length == 0
                ? []
                : [$"Removed undeclared terraform.tfvars assignments: {string.Join(", ", removedTfvarsAssignments.Order(StringComparer.OrdinalIgnoreCase))}."])
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

    private static string RemoveTfvarsAssignments(string content, IReadOnlySet<string> unusedVariables) =>
        RemoveTfvarsAssignments(content, unusedVariables, out _);

    private static string RemoveTfvarsAssignments(string content, IEnumerable<string> variablesToRemove, out string[] removedVariables)
    {
        var variables = variablesToRemove.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var removed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var lines = content.Split(["\r\n", "\n"], StringSplitOptions.None);
        var kept = new List<string>();

        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            var match = TfvarsAssignmentPattern.Match(line);
            if (!match.Success || !variables.Contains(match.Groups["name"].Value))
            {
                kept.Add(line);
                continue;
            }

            removed.Add(match.Groups["name"].Value);
            var depth = CountNestedExpressionDepth(line[(line.IndexOf('=') + 1)..]);
            while (depth > 0 && index + 1 < lines.Length)
            {
                index++;
                depth += CountNestedExpressionDepth(lines[index]);
            }
        }

        removedVariables = removed.ToArray();
        return string.Join(Environment.NewLine, kept).TrimEnd() + Environment.NewLine;
    }

    private static string[] RemoveUndeclaredTfvarsAssignments(
        IDictionary<string, string> files,
        IEnumerable<string> declaredVariables)
    {
        var declared = declaredVariables.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var removedVariables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var tfvarsFile in files.Keys.Where(IsTfvarsFile).ToArray())
        {
            var assignedVariables = TfvarsAssignmentPattern
                .Matches(files[tfvarsFile])
                .Select(match => match.Groups["name"].Value)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var undeclaredVariables = assignedVariables
                .Where(variable => !declared.Contains(variable))
                .ToArray();

            if (undeclaredVariables.Length == 0)
            {
                continue;
            }

            files[tfvarsFile] = RemoveTfvarsAssignments(files[tfvarsFile], undeclaredVariables, out var removed);
            foreach (var variable in removed)
            {
                removedVariables.Add(variable);
            }
        }

        return removedVariables.ToArray();
    }

    private static GeneratedTerraform WithAssumptionIfNeeded(
        GeneratedTerraform terraform,
        IReadOnlyDictionary<string, string> files,
        IReadOnlyList<string> removedTfvarsAssignments)
    {
        if (removedTfvarsAssignments.Count == 0)
        {
            return terraform;
        }

        var assumptions = terraform.Assumptions
            .Concat([$"Removed undeclared terraform.tfvars assignments: {string.Join(", ", removedTfvarsAssignments.Order(StringComparer.OrdinalIgnoreCase))}."])
            .ToArray();

        return new GeneratedTerraform(files, terraform.Summary, assumptions);
    }

    private static int CountNestedExpressionDepth(string text)
    {
        var depth = 0;
        var inString = false;
        var escaped = false;

        foreach (var character in text)
        {
            if (inString)
            {
                escaped = !escaped && character == '\\';
                if (character == '"' && !escaped)
                {
                    inString = false;
                }

                if (character != '\\')
                {
                    escaped = false;
                }

                continue;
            }

            if (character == '"')
            {
                inString = true;
                continue;
            }

            if (character is '{' or '[' or '(')
            {
                depth++;
            }
            else if (character is '}' or ']' or ')')
            {
                depth--;
            }
        }

        return depth;
    }
}
