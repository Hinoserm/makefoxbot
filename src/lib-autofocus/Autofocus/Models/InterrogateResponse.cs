using System.Text.Json.Serialization;

namespace Autofocus.Models;

public interface IInterrogateResult
{
    public string Caption { get; }
}

internal class InterrogateResultResponse
    : IInterrogateResult
{
    [JsonPropertyName("caption")]
    public string Caption { get; init; } = null!;
}