namespace InfraAgent.Core.Intent;

public sealed record InfrastructureIntent(string OriginalPrompt, string AwsRegion, S3BucketIntent? S3Bucket, Ec2InstanceIntent? Ec2Instance, IReadOnlyList<string> Assumptions)
{
    public IReadOnlyList<string> ResourceKinds => new[] { S3Bucket is not null ? "S3" : null, Ec2Instance is not null ? "EC2" : null }.Where(x => x is not null).Cast<string>().ToArray();
}
