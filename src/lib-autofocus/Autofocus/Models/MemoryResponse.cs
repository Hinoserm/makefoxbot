using System.Text.Json.Serialization;

namespace Autofocus.Models;

public interface IMemory
{
    IMemoryRam Ram { get; }
    IMemoryCuda Cuda { get; }
}

public interface IMemoryRam
{
    double Free { get; }
    double Used { get; }
    double Total { get; }
}

public interface IMemoryCuda
{
    IMemoryCudaSystem System { get; }
    IMemoryCudaItem Active { get; }
    IMemoryCudaItem Allocated { get; }
    IMemoryCudaItem Reserved { get; }
    IMemoryCudaItem Inactive { get; }
    IMemoryCudaEvents Events { get; }
}

public interface IMemoryCudaSystem
{
    double Free { get; }
    double Used { get; }
    double Total { get; }
}

public interface IMemoryCudaItem
{
    double Current { get; }
    double Peak { get; }
}

public interface IMemoryCudaEvents
{
    long Retries { get; }
    long OOM { get; }
}

internal class MemoryResponse
    : IMemory
{
    [JsonPropertyName("ram")] public MemoryRamResponse Ram { get; init; } = null!;
    [JsonPropertyName("cuda")] public MemoryCudaResponse Cuda { get; init; } = null!;

    IMemoryRam IMemory.Ram => Ram;
    IMemoryCuda IMemory.Cuda => Cuda;
}

internal class MemoryRamResponse
    : IMemoryRam
{
    [JsonPropertyName("free")] public double Free { get; init; }
    [JsonPropertyName("used")] public double Used { get; init; }
    [JsonPropertyName("total")] public double Total { get; init; }
}

internal class MemoryCudaResponse
    : IMemoryCuda
{
    [JsonPropertyName("system")] public MemoryCudaSystemResponse System { get; init; } = null!;
    [JsonPropertyName("active")] public MemoryCudaItemResponse Active { get; init; } = null!;
    [JsonPropertyName("allocated")] public MemoryCudaItemResponse Allocated { get; init; } = null!;
    [JsonPropertyName("reserved")] public MemoryCudaItemResponse Reserved { get; init; } = null!;
    [JsonPropertyName("inactive")] public MemoryCudaItemResponse Inactive { get; init; } = null!;
    [JsonPropertyName("events")] public MemoryCudaEventsResponse Events { get; init; } = null!;

    IMemoryCudaSystem IMemoryCuda.System => System;
    IMemoryCudaItem IMemoryCuda.Active => Active;
    IMemoryCudaItem IMemoryCuda.Allocated => Allocated;
    IMemoryCudaItem IMemoryCuda.Reserved => Reserved;
    IMemoryCudaItem IMemoryCuda.Inactive => Inactive;
    IMemoryCudaEvents IMemoryCuda.Events => Events;
}

internal class MemoryCudaSystemResponse
    : IMemoryCudaSystem
{
    [JsonPropertyName("free")] public double Free { get; init; }
    [JsonPropertyName("used")] public double Used { get; init; }
    [JsonPropertyName("total")] public double Total { get; init; }
}

internal class MemoryCudaItemResponse
    : IMemoryCudaItem
{
    [JsonPropertyName("current")] public double Current { get; init; }
    [JsonPropertyName("peak")] public double Peak { get; init; }
}

internal class MemoryCudaEventsResponse
    : IMemoryCudaEvents
{
    [JsonPropertyName("retries")] public long Retries { get; init; }
    [JsonPropertyName("oom")] public long OOM { get; init; }
}