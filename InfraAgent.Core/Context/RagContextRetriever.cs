using System.Text.RegularExpressions;
using InfraAgent.Core.Intent;
using InfraAgent.Core.Options;
using Microsoft.Extensions.Options;

namespace InfraAgent.Core.Context;

public sealed class RagContextRetriever(IOptions<RagOptions> options) : IContextRetriever
{
    public async Task<IReadOnlyList<ContextDocument>> RetrieveAsync(InfrastructureIntent intent, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var directory = Path.IsPathRooted(settings.KnowledgeDirectory)
            ? settings.KnowledgeDirectory
            : Path.Combine(AppContext.BaseDirectory, settings.KnowledgeDirectory);

        if (!Directory.Exists(directory)) return [];

        var query = Tokenize($"{intent.OriginalPrompt} {intent.AwsRegion} {string.Join(' ', intent.ResourceKinds)}");
        var candidates = new List<(ContextDocument Document, int Score)>();

        foreach (var file in Directory.EnumerateFiles(directory, "*.md", SearchOption.AllDirectories))
        {
            var content = await File.ReadAllTextAsync(file, cancellationToken);
            foreach (var chunk in Chunk(content, settings.ChunkSizeCharacters, settings.ChunkOverlapCharacters))
            {
                var score = Tokenize($"{Path.GetFileName(file)} {chunk}").Count(query.Contains);
                if (score > 0)
                {
                    candidates.Add((new ContextDocument(Path.GetRelativePath(directory, file), chunk), score));
                }
            }
        }

        return candidates
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Document.Name, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Max(1, settings.TopK))
            .Select(candidate => candidate.Document)
            .ToArray();
    }

    private static HashSet<string> Tokenize(string value) =>
        Regex.Matches(value.ToLowerInvariant(), "[a-z0-9][a-z0-9_-]{2,}")
            .Select(match => match.Value)
            .Where(token => token is not "the" and not "and" and not "for" and not "with")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static IEnumerable<string> Chunk(string content, int size, int overlap)
    {
        size = Math.Max(300, size);
        overlap = Math.Clamp(overlap, 0, size - 1);
        for (var start = 0; start < content.Length; start += size - overlap)
        {
            var length = Math.Min(size, content.Length - start);
            yield return content.Substring(start, length);
            if (start + length >= content.Length) yield break;
        }
    }
}
