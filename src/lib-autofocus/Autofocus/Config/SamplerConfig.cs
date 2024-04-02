using Autofocus.Models;

namespace Autofocus.Config;

public record SamplerConfig
{
    public required ISampler Sampler { get; set; }
    public int? SamplingSteps { get; set; }
    public double? CfgScale { get; set; }
    public double? Eta { get; set; }
}