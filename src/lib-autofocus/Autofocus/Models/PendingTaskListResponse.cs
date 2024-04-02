using System.Text.Json.Serialization;

namespace Autofocus.Models;

internal class PendingTaskListResponse
{
    [JsonPropertyName("size")] public int Size { get; set; }
    [JsonPropertyName("tasks")] public string[] Tasks { get; set; } = [ ];
}