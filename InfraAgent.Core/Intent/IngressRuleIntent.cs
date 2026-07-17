namespace InfraAgent.Core.Intent;

public sealed record IngressRuleIntent(int FromPort, int ToPort, string Protocol, string CidrBlock, string Description);
