namespace InfraAgent.Core.Intent;

public sealed record S3BucketIntent(string LogicalName, string BucketName, bool VersioningEnabled, bool BlockPublicAccess, bool ServerSideEncryptionEnabled, string EncryptionAlgorithm = "AES256", bool LifecycleEnabled = false, int LifecycleTransitionDays = 30, Dictionary<string, string>? Tags = null)
{
    public S3BucketIntent(string LogicalName, bool VersioningEnabled, bool BlockPublicAccess, bool ServerSideEncryptionEnabled)
        : this(LogicalName, "infra-agent-bucket", VersioningEnabled, BlockPublicAccess, ServerSideEncryptionEnabled) { }
}
