namespace InfraAgent.Core.Options;

public sealed class RagOptions
{
    public string KnowledgeDirectory { get; set; } = "Knowledge";
    public int TopK { get; set; } = 5;
    public int ChunkSizeCharacters { get; set; } = 1800;
    public int ChunkOverlapCharacters { get; set; } = 200;
}
