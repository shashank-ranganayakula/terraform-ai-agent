namespace InfraAgent.Core.Intent;

public sealed record Ec2InstanceIntent(
    string LogicalName,
    string InstanceType,
    string AmiNamePattern,
    IReadOnlyList<IngressRuleIntent> IngressRules);
