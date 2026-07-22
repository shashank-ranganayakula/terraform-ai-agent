using InfraAgent.Core.Intent;

namespace InfraAgent.Core.Context;

public sealed class FileContextRetriever : IContextRetriever
{
    public async Task<IReadOnlyList<ContextDocument>> RetrieveAsync(InfrastructureIntent intent, CancellationToken cancellationToken)
    {
        var contextDirectory = Path.Combine(AppContext.BaseDirectory, "Context");
        if (!Directory.Exists(contextDirectory))
        {
            contextDirectory = Path.Combine(Directory.GetCurrentDirectory(), "Context");
        }

        var files = new List<string>();
        if (intent.S3Bucket is not null)
        {
            files.Add(Path.Combine(contextDirectory, "aws_s3.md"));
        }

        if (intent.Ec2Instance is not null)
        {
            files.Add(Path.Combine(contextDirectory, "aws_ec2.md"));
        }

        var documents = new List<ContextDocument>();
        foreach (var file in files.Where(File.Exists))
        {
            documents.Add(new ContextDocument(Path.GetFileName(file), await File.ReadAllTextAsync(file, cancellationToken)));
        }

        return documents;
    }
}
