using System.Net;
using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using InfraAgent.Core.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InfraAgent.Core.Preflight;

public sealed class S3BucketAvailabilityChecker(
    IOptions<AgentOptions> options,
    ILogger<S3BucketAvailabilityChecker> logger) : IS3BucketAvailabilityChecker
{
    public async Task<S3BucketAvailabilityResult> CheckAsync(
        string bucketName,
        string awsRegion,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(bucketName))
        {
            return S3BucketAvailabilityResult.Available(bucketName);
        }

        var timeoutSeconds = Math.Max(1, options.Value.S3BucketPreflightTimeoutSeconds);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

        try
        {
            logger.LogInformation("Checking S3 bucket name availability for {BucketName} in {AwsRegion}", bucketName, awsRegion);

            using var client = new AmazonS3Client(RegionEndpoint.GetBySystemName(awsRegion));
            await client.HeadBucketAsync(new HeadBucketRequest { BucketName = bucketName }, linked.Token);

            return S3BucketAvailabilityResult.Exists(bucketName, awsRegion);
        }
        catch (AmazonS3Exception ex) when (IsBucketMissing(ex))
        {
            return S3BucketAvailabilityResult.Available(bucketName);
        }
        catch (AmazonS3Exception ex) when (IsBucketTaken(ex))
        {
            logger.LogInformation(
                ex,
                "S3 bucket name {BucketName} exists or is not accessible. StatusCode: {StatusCode}, ErrorCode: {ErrorCode}",
                bucketName,
                ex.StatusCode,
                ex.ErrorCode);

            return S3BucketAvailabilityResult.Exists(bucketName, awsRegion);
        }
        catch (AmazonServiceException ex) when (IsAuthenticationFailure(ex))
        {
            return S3BucketAvailabilityResult.Failed(
                $"AWS authentication failed while checking S3 bucket '{bucketName}': {ex.Message}");
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            return S3BucketAvailabilityResult.Failed(
                $"S3 bucket preflight timeout after {timeoutSeconds} seconds while checking '{bucketName}'.");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "S3 bucket preflight check failed for {BucketName}", bucketName);
            return S3BucketAvailabilityResult.Failed(
                $"AWS S3 bucket preflight check failed for '{bucketName}': {ex.Message}");
        }
    }

    private static bool IsBucketMissing(AmazonS3Exception ex) =>
        ex.StatusCode == HttpStatusCode.NotFound ||
        string.Equals(ex.ErrorCode, "NoSuchBucket", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(ex.ErrorCode, "NotFound", StringComparison.OrdinalIgnoreCase);

    private static bool IsBucketTaken(AmazonS3Exception ex) =>
        ex.StatusCode is HttpStatusCode.OK or HttpStatusCode.Forbidden or HttpStatusCode.MovedPermanently ||
        string.Equals(ex.ErrorCode, "AccessDenied", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(ex.ErrorCode, "Forbidden", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(ex.ErrorCode, "PermanentRedirect", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(ex.ErrorCode, "AuthorizationHeaderMalformed", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(ex.ErrorCode, "BucketAlreadyExists", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(ex.ErrorCode, "BucketAlreadyOwnedByYou", StringComparison.OrdinalIgnoreCase);

    private static bool IsAuthenticationFailure(AmazonServiceException ex) =>
        ex.StatusCode == HttpStatusCode.Unauthorized ||
        string.Equals(ex.ErrorCode, "InvalidAccessKeyId", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(ex.ErrorCode, "InvalidClientTokenId", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(ex.ErrorCode, "ExpiredToken", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(ex.ErrorCode, "UnrecognizedClientException", StringComparison.OrdinalIgnoreCase);
}
