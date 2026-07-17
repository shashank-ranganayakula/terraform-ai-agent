namespace InfraAgent.Core.Intent;

public sealed record IntentParseResult(InfrastructureIntent? Intent, string? ClarifyingQuestion)
{
    public bool NeedsClarification => ClarifyingQuestion is not null;

    public static IntentParseResult Clarify(string question) => new(null, question);

    public static IntentParseResult Complete(InfrastructureIntent intent) => new(intent, null);
}
