using System.Text.Json.Serialization;

namespace Autofocus.Models;

public interface IProgress
{
    public double Progress { get; }
    public TimeSpan ETARelative { get; }
    public IProgressState State { get; }

    public Base64EncodedImage? CurrentImage { get; }
}

public interface IProgressState
{
    public bool Skipped { get; }
    public bool Interrupted { get; }
    public int JobCount { get; }
    public int JobNum { get; }
    public int SamplingStep { get; }
    public int SamplingSteps { get; }
}

internal class ProgressResponse
    : IProgress
{
    [JsonPropertyName("progress")] public double Progress { get; init; }
    [JsonPropertyName("eta_relative")] public double EtaRelative { get; init; }
    [JsonPropertyName("state")] public ProgressStateResponse State { get; init; } = null!;
    [JsonPropertyName("current_image")] public Base64EncodedImage? CurrentImage { get; init; }

    double IProgress.Progress => Progress;
    TimeSpan IProgress.ETARelative => TimeSpan.FromSeconds(Progress);
    IProgressState IProgress.State => State;
    Base64EncodedImage? IProgress.CurrentImage => CurrentImage;
}

internal class ProgressStateResponse
    : IProgressState
{
    [JsonPropertyName("skipped")] public bool Skipped { get; init; }
    [JsonPropertyName("interrupted")] public bool Interrupted { get; init; }
    [JsonPropertyName("job_count")] public int JobCount { get; init; }
    [JsonPropertyName("job_no")] public int JobNum { get; init; }
    [JsonPropertyName("sampling_step")] public int SamplingStep { get; init; }
    [JsonPropertyName("sampling_steps")] public int SamplingSteps { get; init; }
}