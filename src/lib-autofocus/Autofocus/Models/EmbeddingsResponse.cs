using System.Text.Json.Serialization;

namespace Autofocus.Models;

public interface IEmbeddings
{
    public IReadOnlyDictionary<string, IEmbedding> Loaded { get; }
    public IReadOnlyDictionary<string, IEmbedding> Skipped { get; }
    public IReadOnlyDictionary<string, IEmbedding> All { get; }
}

public interface IEmbedding
{
    public string Name { get; }

    public int? Step { get; }
    public string Checkpoint { get; }
    public string CheckpointName { get; }
    public int Shape { get; }
    public int Vectors { get; }
}

internal class EmbeddingsResponse
    : IEmbeddings
{
    [JsonPropertyName("loaded")] public Dictionary<string, EmbeddingResponse> Loaded { get; init; } = null!;
    [JsonPropertyName("skipped")] public Dictionary<string, EmbeddingResponse> Skipped { get; init; } = null!;

    private IReadOnlyDictionary<string, IEmbedding>? _loaded;
    private IReadOnlyDictionary<string, IEmbedding>? _skipped;
    private IReadOnlyDictionary<string, IEmbedding>? _all;

    IReadOnlyDictionary<string, IEmbedding> IEmbeddings.Loaded
    {
        get
        {
            Load();
            return _loaded!;
        }
    }
    IReadOnlyDictionary<string, IEmbedding> IEmbeddings.Skipped
    {
        get
        {
            Load();
            return _skipped!;
        }
    }
    IReadOnlyDictionary<string, IEmbedding> IEmbeddings.All
    {
        get
        {
            Load();
            return _all!;
        }
    }

    private void Load()
    {
        if (_loaded == null)
        {
            var d = new Dictionary<string, IEmbedding>();
            foreach (var (key, value) in Loaded)
                d.Add(key, value.Bind(key));
            _loaded = d;
        }

        if (_skipped == null)
        {
            var d = new Dictionary<string, IEmbedding>();
            foreach (var (key, value) in Skipped)
                d.Add(key, value.Bind(key));
            _skipped = d;
        }

        var all = new Dictionary<string, IEmbedding>();
        _all = all;
        foreach (var (k, v) in _loaded)
            all.Add(k, v);
        foreach (var (k, v) in _skipped)
            all.Add(k, v);
    }
}

internal class EmbeddingResponse
    : IEmbedding
{
    private string? _name;
    string IEmbedding.Name => _name!;

    [JsonPropertyName("step")] public int? Step { get; init; } = null!;
    [JsonPropertyName("sd_checkpoint")] public string Checkpoint { get; init; } = null!;
    [JsonPropertyName("sd_checkpoint_name")] public string CheckpointName { get; init; } = null!;
    [JsonPropertyName("shape")] public int Shape { get; init; }
    [JsonPropertyName("vectors")] public int Vectors { get; init; }

    internal EmbeddingResponse Bind(string name)
    {
        _name = name;
        return this;
    }
}
