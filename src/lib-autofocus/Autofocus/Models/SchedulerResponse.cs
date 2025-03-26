using System.Text.Json.Serialization;

namespace Autofocus.Models;

public interface IScheduler
{
    string Name { get; }
    string Label { get; }
    IReadOnlyList<string> Aliases { get; }
    double DefaultRho { get; }
    bool NeedInnerModel { get; }
}
internal class SchedulerResponse : IScheduler
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = null!;

    [JsonPropertyName("label")]
    public string Label { get; init; } = null!;

    [JsonPropertyName("aliases")]
    public string[] Aliases { get; init; } = [];

    [JsonPropertyName("default_rho")]
    public double DefaultRho { get; init; }

    [JsonPropertyName("need_inner_model")]
    public bool NeedInnerModel { get; init; }

    IReadOnlyList<string> IScheduler.Aliases => Aliases;
}