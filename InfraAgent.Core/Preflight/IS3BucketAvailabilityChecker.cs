namespace InfraAgent.Core.Preflight;

public interface IS3BucketAvailabilityChecker
{
    Task<S3BucketAvailabilityResult> CheckAsync(
        string bucketName,
        string awsRegion,
        CancellationToken cancellationToken);
}
