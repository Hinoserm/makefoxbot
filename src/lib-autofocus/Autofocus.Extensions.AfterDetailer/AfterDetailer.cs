using Autofocus.Config;

namespace Autofocus.Extensions.AfterDetailer;

public record AfterDetailer
    : IAdditionalScriptConfig
{
    public List<Step> Steps { get; set; } = new();

    public string Key => "ADetailer";

    public object ToJsonObject()
    {
        var args = Steps.Select(step => step.ToJsonObject()).ToList();

        return new { args };
    }
}