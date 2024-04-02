using System.Text.Json.Serialization;

namespace Autofocus.Models;

public interface IQueueStatus
{
    public int QueueSize { get; }
}

internal class QueueStatusResponse
    : IQueueStatus
{
    // "msg": "estimation",
    // "rank": null,
    // "avg_event_process_time": 0,
    // "avg_event_concurrent_process_time": null,
    // "rank_eta": null,
    // "queue_eta": 1

    [JsonPropertyName("queue_size")] public int QueueSize { get; init; }
}