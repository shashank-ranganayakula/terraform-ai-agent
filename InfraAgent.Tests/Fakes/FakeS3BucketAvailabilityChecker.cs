using InfraAgent.Core.Preflight;

namespace InfraAgent.Tests.Fakes;

public sealed class FakeS3BucketAvailabilityChecker(S3BucketAvailabilityResult? result = null) : IS3BucketAvailabilityChecker
{
    private readonly S3BucketAvailabilityResult _result = result ?? S3BucketAvailabilityResult.Available("test-bucket");

    public int Calls { get; private set; }

    public Task<S3BucketAvailabilityResult> CheckAsync(
        string bucketName,
        string awsRegion,
        CancellationToken cancellationToken)
    {
        Calls++;
        return Task.FromResult(_result);
    }
}
