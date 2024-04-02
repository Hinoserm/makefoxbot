namespace Autofocus.Scripts;

public interface IScriptConfig
{
    string Key { get; }

    object? ToJsonArgs();
}