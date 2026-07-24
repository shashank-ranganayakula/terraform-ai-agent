namespace InfraAgent.Core.Preflight;

public sealed record S3BucketAvailabilityResult(
    S3BucketAvailabilityStatus Status,
    string Message)
{
    public static S3BucketAvailabilityResult Available(string bucketName) =>
        new(S3BucketAvailabilityStatus.Available, $"S3 bucket name '{bucketName}' is available.");

    public static S3BucketAvailabilityResult Exists(string bucketName, string awsRegion) =>
        new(
            S3BucketAvailabilityStatus.Exists,
            $"S3 bucket name '{bucketName}' already exists and cannot be created in {awsRegion}. Choose a globally unique bucket name.");

    public static S3BucketAvailabilityResult Failed(string message) =>
        new(S3BucketAvailabilityStatus.CheckFailed, message);
}
