using Autofocus.Models;

namespace Autofocus.Config;

public record PromptConfig
{
    public required string Positive { get; set; }
    public required string Negative { get; set; }
    public List<IPromptStyle> Styles { get; init;  } = new();
}