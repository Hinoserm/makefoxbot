using Autofocus.Models;

namespace Autofocus.Config;

public record SchedulerConfig
{
    public required IScheduler Scheduler { get; set; }
    public double? Rho { get; set; }
}
