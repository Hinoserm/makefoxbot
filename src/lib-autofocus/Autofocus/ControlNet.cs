using System.Net.Http.Json;
using System.Text.Json;
using Autofocus.CtrlNet;

namespace Autofocus;

public class ControlNet
{
    internal static readonly JsonSerializerOptions SerializerOptions = StableDiffusion.SerializerOptions;

    private readonly StableDiffusion _api;

    internal ControlNet(StableDiffusion api)
    {
        _api = api;
    }

	#region models
	/// <exception cref="HttpRequestException"/>
	/// <exception cref="OperationCanceledException"/>
	public async Task<IEnumerable<ControlNetModel>> Models(CancellationToken cancellationToken = default)
    {
        var response = await _api.FastHttpClient.GetFromJsonAsync<ControlNetModelListResponse>("controlnet/model_list", SerializerOptions, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        return response!.ModelList.Select(a => new ControlNetModel(a));
    }

    /// <exception cref="HttpRequestException"/>
    /// <exception cref="InvalidOperationException"/>
    /// <exception cref="OperationCanceledException"/>
    public async Task<ControlNetModel?> Model(string name, CancellationToken cancellationToken = default)
    {
        var models = await Models(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        return models.SingleOrDefault(IsMatch);

        bool IsMatch(ControlNetModel a)
        {
            if (a.Name.Equals(name, StringComparison.InvariantCultureIgnoreCase))
                return true;

            var idx = a.Name.IndexOf(" [", StringComparison.InvariantCultureIgnoreCase);
            if (idx < 0)
                return false;

            var cleaned = a.Name[..idx];
            return cleaned.Equals(name, StringComparison.InvariantCultureIgnoreCase);
        }
    }
	#endregion

	#region modules
	/// <exception cref="HttpRequestException"/>
	/// <exception cref="OperationCanceledException"/>
	public async Task<IEnumerable<ControlNetModule>> Modules(CancellationToken cancellationToken = default)
    {
        var response = await _api.FastHttpClient.GetFromJsonAsync<ControlNetModuleListResponse>("controlnet/module_list", SerializerOptions, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        return response!.Details.Select(a =>
            new ControlNetModule(
                a.Key,
                Parameter(a.Value.Sliders.FirstOrDefault(), true),
                a.Value.Sliders.Skip(1).Select(x => Parameter(x)!).ToArray()
            )
        );

        static ControlNetModule.Parameter? Parameter(ControlnetModuleDetailSliderResponse? slider, bool allownull = false)
        {
            if (slider == null)
            {
                if (allownull)
                    return null;
                throw new ArgumentNullException(nameof(slider));
            }

            return new ControlNetModule.Parameter(slider.Name, slider.Value, slider.Min, slider.Max);
        }
    }

    /// <exception cref="HttpRequestException"/>
    /// <exception cref="InvalidOperationException"/>
    /// <exception cref="OperationCanceledException"/>
    public async Task<ControlNetModule> Module(string name, CancellationToken cancellationToken = default)
    {
        var models = await Modules(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        return models.Single(a => a.Name.Equals(name, StringComparison.InvariantCultureIgnoreCase));
    }
	#endregion

	/// <exception cref="HttpRequestException"/>
	/// <exception cref="OperationCanceledException"/>
	public async Task<IControlNetPreprocess> Preprocess(ControlNetPreprocessConfig config, CancellationToken cancellationToken = default)
    {
        var request = await _api.FastHttpClient.PostAsJsonAsync("controlnet/detect", new ControlNetPreprocessConfigModel(config), SerializerOptions, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        var response = await request
            .EnsureSuccessStatusCode()
            .Content
            .ReadFromJsonAsync<ControlNetPreprocessResponse>(SerializerOptions, cancellationToken);

        return response!;
    }
}