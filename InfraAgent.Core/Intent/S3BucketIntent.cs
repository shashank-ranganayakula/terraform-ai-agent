namespace InfraAgent.Core.Intent;

public sealed record S3BucketIntent(
    string LogicalName,
    bool VersioningEnabled,
    bool BlockPublicAccess,
    bool ServerSideEncryptionEnabled);
