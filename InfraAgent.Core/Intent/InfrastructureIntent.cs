namespace InfraAgent.Core.Intent;

public sealed record InfrastructureIntent(
    string OriginalPrompt,
    string AwsRegion,
    S3BucketIntent? S3Bucket,
    Ec2InstanceIntent? Ec2Instance,
    IReadOnlyList<string> Assumptions)
{
    public IReadOnlyList<ResourceKind> ResourceKinds
    {
        get
        {
            var kinds = new List<ResourceKind>();
            if (S3Bucket is not null)
            {
                kinds.Add(ResourceKind.S3Bucket);
            }

            if (Ec2Instance is not null)
            {
                kinds.Add(ResourceKind.Ec2Instance);
            }

            return kinds;
        }
    }
}
