using Autofocus.Config;
using Autofocus.Models;
using Autofocus.Scripts;

namespace Autofocus;

public interface IStableDiffusion
{
    /// <exception cref="HttpRequestException"/>
    /// <exception cref="OperationCanceledException"/>
    public Task<IProgress> Progress(bool skipCurrentImage = false, CancellationToken cancellationToken = default);

    /// <exception cref="HttpRequestException"/>
    /// <exception cref="OperationCanceledException"/>
    public Task Ping(CancellationToken cancellationToken = default);

    /// <exception cref="HttpRequestException"/>
    /// <exception cref="OperationCanceledException"/>
    public Task<IQueueStatus> QueueStatus(CancellationToken cancellationToken = default);

    /// <exception cref="HttpRequestException"/>
    /// <exception cref="OperationCanceledException"/>
    public Task<IScriptsResponse> Scripts(CancellationToken cancellationToken = default);

    /// <exception cref="HttpRequestException"/>
    /// <exception cref="OperationCanceledException"/>
    public Task<IEnumerable<ISampler>> Samplers(CancellationToken cancellationToken = default);

    /// <exception cref="HttpRequestException"/>
    /// <exception cref="InvalidOperationException"/>  
    /// <exception cref="OperationCanceledException"/>
    public Task<ISampler> Sampler(string name, CancellationToken cancellationToken = default);

    /// <exception cref="HttpRequestException"/>
    /// <exception cref="OperationCanceledException"/>
    public Task<IEnumerable<IUpscaler>> Upscalers(CancellationToken cancellationToken = default);

    /// <exception cref="HttpRequestException"/>
    /// <exception cref="InvalidOperationException"/>
    /// <exception cref="OperationCanceledException"/>
    public Task<IUpscaler> Upscaler(string name, CancellationToken cancellationToken = default);

    /// <exception cref="HttpRequestException"/>
    /// <exception cref="OperationCanceledException"/>
    public Task<IEnumerable<IPromptStyle>> Styles(CancellationToken cancellationToken = default);

    /// <exception cref="HttpRequestException"/>
    /// <exception cref="InvalidOperationException"/>
    /// <exception cref="OperationCanceledException"/>
    public Task<IPromptStyle> Style(string name, CancellationToken cancellationToken = default);

    /// <exception cref="HttpRequestException"/>
    /// <exception cref="OperationCanceledException"/>
    public Task<IEnumerable<IStableDiffusionModel>> StableDiffusionModels(CancellationToken cancellationToken = default);

    /// <exception cref="HttpRequestException"/>
    /// <exception cref="InvalidOperationException"/>
    /// <exception cref="OperationCanceledException"/>
    public Task<IStableDiffusionModel> StableDiffusionModel(string name, CancellationToken cancellationToken = default);

    /// <exception cref="HttpRequestException"/>
    /// <exception cref="OperationCanceledException"/>
    public Task<IEmbeddings> Embeddings(CancellationToken cancellationToken = default);

    /// <exception cref="HttpRequestException"/>
    /// <exception cref="OperationCanceledException"/>
    public Task<IMemory> Memory(CancellationToken cancellationToken = default);

    /// <exception cref="HttpRequestException"/>
    /// <exception cref="OperationCanceledException"/>
    public Task<IPngInfo> PngInfo(Base64EncodedImage image, CancellationToken cancellationToken = default);

    /// <exception cref="HttpRequestException"/>
    /// <exception cref="OperationCanceledException"/>
    /// <exception cref="ScriptNotFoundException"/>
    public Task<ITextToImageResult> TextToImage(TextToImageConfig config, CancellationToken cancellationToken = default);

    /// <exception cref="HttpRequestException"/>
    /// <exception cref="OperationCanceledException"/>
    /// <exception cref="ScriptNotFoundException"/>
    public Task<IImageToImageResult> Image2Image(ImageToImageConfig config, CancellationToken cancellationToken = default);

    /// <exception cref="HttpRequestException"/>
    /// <exception cref="OperationCanceledException"/>
    public Task<IInterrogateResult> Interrogate(Base64EncodedImage image, InterrogateModel model = InterrogateModel.CLIP, CancellationToken cancellationToken = default);

    /// <exception cref="HttpRequestException"/>
    /// <exception cref="OperationCanceledException"/>
    public Task<IInterrogateResult> Interrogate(InterrogateConfig config, CancellationToken cancellationToken = default);

    /// <exception cref="HttpRequestException"/>
    /// <exception cref="InvalidOperationException"/>
    /// <exception cref="OperationCanceledException"/>
    public Task<ControlNet?> TryGetControlNet(CancellationToken cancellationToken = default);

    /// <summary>
    /// Get a list of all pending tasks
    /// </summary>
    /// <returns></returns>
    /// <exception cref="HttpRequestException"/>
    /// <exception cref="InvalidOperationException"/>
    /// <exception cref="OperationCanceledException"/>
    public Task<IReadOnlyList<string>> PendingTasks(CancellationToken cancellationToken = default);
}