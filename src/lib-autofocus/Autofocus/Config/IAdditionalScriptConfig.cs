namespace Autofocus.Config;

public interface IAdditionalScriptConfig
{
    string Key { get; }

    object ToJsonObject();
}