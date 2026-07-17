namespace InfraAgent.Core.Intent;

public interface IIntentParser
{
    IntentParseResult Parse(string prompt);
}
