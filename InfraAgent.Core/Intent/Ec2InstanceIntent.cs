namespace InfraAgent.Core.Intent;

public sealed record Ec2InstanceIntent(string LogicalName, string InstanceType, string AmiNamePattern, IReadOnlyList<IngressRuleIntent> IngressRules, string InstanceName, Dictionary<string, string>? Tags = null)
{
    public Ec2InstanceIntent(string LogicalName, string InstanceType, string AmiNamePattern, IReadOnlyList<IngressRuleIntent> IngressRules)
        : this(LogicalName, InstanceType, AmiNamePattern, IngressRules, "web-server") { }
}
