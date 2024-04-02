using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Autofocus.Config;
using Autofocus.CtrlNet;
using Autofocus.Models;
using Autofocus.Scripts;

namespace Autofocus;

public class StableDiffusion
    : IStableDiffusion
{
    internal static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        IncludeFields = true,
        Converters =
        {
            new Base64EncodedImageConverter()
        }
    };

    private HttpClient SlowHttpClient { get; }
    internal HttpClient FastHttpClient { get; }

    /// <summary>
    /// Timeout used for "fast" operations (anything that's not image processing)
    /// </summary>
    public TimeSpan TimeoutFast
    {
        get => FastHttpClient.Timeout;
        init => FastHttpClient.Timeout = value;
    }

    /// <summary>
    /// Timeout used for "slow" operations
    /// </summary>
    public TimeSpan TimeoutSlow
    {
        get => SlowHttpClient.Timeout;
        init => SlowHttpClient.Timeout = value;
    }

    public StableDiffusion(string? address = null)
        : this(address == null ? new Uri("http://127.0.0.1:7860") : new Uri(address))
    {
    }

    public StableDiffusion(Uri address, IHttpClientFactory? factory = null, string? httpClientName = null)
    {
        if (factory == null)
        {
            SlowHttpClient = new();
            FastHttpClient = new();
        }
        else
        {
            SlowHttpClient = httpClientName == null ? factory.CreateClient() : factory.CreateClient(httpClientName);
            FastHttpClient = httpClientName == null ? factory.CreateClient() : factory.CreateClient(httpClientName);
        }

        SlowHttpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Autofocus Agent");
        FastHttpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Autofocus Agent");

        if (!string.IsNullOrEmpty(address.UserInfo))
        {
            var base64 = Convert.ToBase64String(Encoding.ASCII.GetBytes(address.UserInfo));
            SlowHttpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", base64);
            FastHttpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", base64);

            var builder = new UriBuilder(address) { UserName = null, Password = null };
            var cleanAddress = builder.Uri;
            SlowHttpClient.BaseAddress = cleanAddress;
            FastHttpClient.BaseAddress = cleanAddress;
        }
        else
        {
            SlowHttpClient.BaseAddress = address;
            FastHttpClient.BaseAddress = address;
        }
    }

    /// <inheritdoc />
    public async Task<IProgress> Progress(bool skipCurrentImage = false, CancellationToken cancellationToken = default)
    {
        return (await FastHttpClient.GetFromJsonAsync<ProgressResponse>(
            $"/sdapi/v1/progress?skip_current_image={skipCurrentImage}",
            SerializerOptions,
            cancellationToken
        ))!;
    }

    public async Task<IReadOnlyList<string>> PendingTasks(CancellationToken cancellationToken = default)
    {
        return (await FastHttpClient.GetFromJsonAsync<PendingTaskListResponse>(
            "/internal/pending-tasks",
            SerializerOptions,
            cancellationToken
        ))!.Tasks;
    }

    /// <inheritdoc />
    public async Task Ping(CancellationToken cancellationToken = default)
    {
        (await FastHttpClient.GetAsync("/internal/ping", cancellationToken)).EnsureSuccessStatusCode();
    }

    /// <inheritdoc />
    public async Task<IQueueStatus> QueueStatus(CancellationToken cancellationToken = default)
    {
        return (await FastHttpClient.GetFromJsonAsync<QueueStatusResponse>("/queue/status", SerializerOptions, cancellationToken))!;
    }

    #region scripts
    /// <inheritdoc />
    public async Task<IScriptsResponse> Scripts(CancellationToken cancellationToken = default)
    {
        return (await FastHttpClient.GetFromJsonAsync<ScriptsResponse>("/sdapi/v1/scripts", SerializerOptions, cancellationToken))!;
    }
    #endregion

    #region sampler
    /// <inheritdoc />
    public async Task<IEnumerable<ISampler>> Samplers(CancellationToken cancellationToken = default)
    {
        return (await FastHttpClient.GetFromJsonAsync<SamplerResponse[]>("/sdapi/v1/samplers", SerializerOptions, cancellationToken))!;
    }

    /// <inheritdoc />
    public async Task<ISampler> Sampler(string name, CancellationToken cancellationToken = default)
    {
        var samplers = await Samplers(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        return samplers.Single(a => a.Name.Equals(name, StringComparison.InvariantCultureIgnoreCase));
    }
    #endregion

    #region upscaler
    /// <inheritdoc />
    public async Task<IEnumerable<IUpscaler>> Upscalers(CancellationToken cancellationToken = default)
    {
        var upscalers = await FastHttpClient.GetFromJsonAsync<UpscalerResponse[]>("/sdapi/v1/upscalers", SerializerOptions, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        for (var i = 0; i < upscalers!.Length; i++)
            upscalers[i].Index = i;

        return upscalers;
    }

    /// <inheritdoc />
    public async Task<IUpscaler> Upscaler(string name, CancellationToken cancellationToken = default)
    {
        var models = await Upscalers(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        return models.Single(a => a.Name.Equals(name, StringComparison.InvariantCultureIgnoreCase));
    }
    #endregion

    #region style
    /// <inheritdoc />
    public async Task<IEnumerable<IPromptStyle>> Styles(CancellationToken cancellationToken)
    {
        return (await FastHttpClient.GetFromJsonAsync<PromptStyleResponse[]>("/sdapi/v1/prompt-styles", SerializerOptions, cancellationToken))!;
    }

    /// <inheritdoc />
    public async Task<IPromptStyle> Style(string name, CancellationToken cancellationToken = default)
    {
        var styles = await Styles(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        return styles.Single(a => a.Name.Equals(name, StringComparison.InvariantCultureIgnoreCase));
    }
    #endregion

    #region SD models
    /// <inheritdoc />
    public async Task<IEnumerable<IStableDiffusionModel>> StableDiffusionModels(CancellationToken cancellationToken = default)
    {
        return (await FastHttpClient.GetFromJsonAsync<StableDiffusionModelResponse[]>("/sdapi/v1/sd-models", SerializerOptions, cancellationToken))!;
    }

    /// <inheritdoc />
    public async Task<IStableDiffusionModel> StableDiffusionModel(string name, CancellationToken cancellationToken = default)
    {
        var models = await StableDiffusionModels(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        return models.Single(a => a.ModelName.Equals(name, StringComparison.InvariantCultureIgnoreCase));
    }
    #endregion

    #region embeddings
    /// <inheritdoc />
    public async Task<IEmbeddings> Embeddings(CancellationToken cancellationToken = default)
    {
        return (await FastHttpClient.GetFromJsonAsync<EmbeddingsResponse>("/sdapi/v1/embeddings", SerializerOptions, cancellationToken))!;
    }
    #endregion

    #region memory
    /// <inheritdoc />
    public async Task<IMemory> Memory(CancellationToken cancellationToken = default)
    {
        return (await FastHttpClient.GetFromJsonAsync<MemoryResponse>("/sdapi/v1/memory", SerializerOptions, cancellationToken))!;
    }
    #endregion

    #region PNG Info
    /// <inheritdoc />
    public async Task<IPngInfo> PngInfo(Base64EncodedImage image, CancellationToken cancellationToken = default)
    {
        var request = new PngInfoRequest(image);

        var response = await FastHttpClient.PostAsJsonAsync("/sdapi/v1/png-info", request, SerializerOptions, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        var result = await response
            .EnsureSuccessStatusCode()
            .Content
            .ReadFromJsonAsync<PngInfoResponse>(cancellationToken: cancellationToken);

        return result!;
    }
	#endregion

	#region Text2Image
	/// <exception cref="HttpRequestException"/>
	/// <exception cref="OperationCanceledException"/>
	/// <exception cref="ScriptNotFoundException"/>
	private async Task CheckTxt2ImgScript(string? name, CancellationToken cancellationToken)
    {
        if (name == null)
            return;

        var scripts = await Scripts(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        if (!scripts.Txt2Img.Contains(name))
            throw new ScriptNotFoundException(name);
    }

    /// <inheritdoc />
    public async Task<ITextToImageResult> TextToImage(TextToImageConfig config, CancellationToken cancellationToken = default)
    {
        await CheckTxt2ImgScript(config.Script?.Key, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        var request = new TextToImageConfigRequest(config);

        var response = await SlowHttpClient.PostAsJsonAsync("/sdapi/v1/txt2img", request, SerializerOptions, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        var result = await response
                          .EnsureSuccessStatusCode()
                          .Content
                          .ReadFromJsonAsync<TextToImageResultResponse>(SerializerOptions, cancellationToken);

        return result!;
    }
	#endregion

	#region Image2Image
	/// <exception cref="HttpRequestException"/>
	/// <exception cref="OperationCanceledException"/>
	/// <exception cref="ScriptNotFoundException"/>
	private async Task CheckImg2ImgScript(string? name, CancellationToken cancellationToken)
    {
        if (name == null)
            return;

        var scripts = await Scripts(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        if (!scripts.Img2Img.Contains(name))
            throw new ScriptNotFoundException(name);
    }

    /// <inheritdoc />
    public async Task<IImageToImageResult> Image2Image(ImageToImageConfig config, CancellationToken cancellationToken = default)
    {
        await CheckImg2ImgScript(config.Script?.Key, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        var request = new ImageToImageConfigRequest(config);

        var response = await SlowHttpClient.PostAsJsonAsync("/sdapi/v1/img2img", request, SerializerOptions, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        var result = await response
                          .EnsureSuccessStatusCode()
                          .Content
                          .ReadFromJsonAsync<ImageToImageResultResponse>(SerializerOptions, cancellationToken);

        return result!;
    }
    #endregion

    #region interrogate
    /// <inheritdoc />
    public Task<IInterrogateResult> Interrogate(Base64EncodedImage image, InterrogateModel model = InterrogateModel.CLIP, CancellationToken cancellationToken = default)
    {
        return Interrogate(new InterrogateConfig
        {
            Image = image,
            Model = model,
        }, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IInterrogateResult> Interrogate(InterrogateConfig config, CancellationToken cancellationToken = default)
    {
        var request = new InterrogateConfigRequest(config);

        var response = await SlowHttpClient.PostAsJsonAsync("/sdapi/v1/interrogate", request, SerializerOptions, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        var result = await response
                          .EnsureSuccessStatusCode()
                          .Content
                          .ReadFromJsonAsync<InterrogateResultResponse>(SerializerOptions, cancellationToken);

        return result!;
    }
    #endregion

    #region controlnet
    /// <inheritdoc />
    public async Task<ControlNet?> TryGetControlNet(CancellationToken cancellationToken = default)
    {
        // Get version of controlnet
        var versionResponse = await FastHttpClient.GetAsync("/controlnet/version", cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        // Check if exists at all!
        if (versionResponse.StatusCode == HttpStatusCode.NotFound)
            return null;

        // Check that it is v2
        var version = await JsonSerializer.DeserializeAsync<ControlNetVersionResponse>(
            await versionResponse.EnsureSuccessStatusCode().Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken
        );
        if (version is not { Version: 2 })
            throw new InvalidOperationException($"Unknown controlnet version: '{version?.Version}'");

        return new ControlNet(this);
    }
    #endregion
}