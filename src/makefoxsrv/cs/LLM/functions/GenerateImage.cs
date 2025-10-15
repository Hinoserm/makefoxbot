using System.Threading.Tasks;

namespace makefoxsrv.llm.functions
{
    public static class ImageFunctions
    {
        [LLMFunction("Generates an image based on a descriptive prompt and optional parameters.")]
        public static async Task GenerateImage(
            FoxTelegram t,
            FoxUser user,
            [LLMParam("The prompt describing the image to generate.")] string Prompt,
            [LLMParam("A negative prompt describing what to avoid in the image.", false)] string? NegativePrompt = null,
            [LLMParam("Width of the generated image, between 512 and 1024. Default: 640", false)] int Width = 640,
            [LLMParam("Height of the generated image, between 512 and 1024. Default: 768", false)] int Height = 768,
            [LLMParam("The name of the model to use.", false)] string? Model = null,
            [LLMParam("An optional list of LORAs to help with image generation.", false)] List<string>? LORAs = null)
        {
            // Fetch current user settings for image generation
            var settings = await FoxUserSettings.GetTelegramSettings(user, t.User, t.Chat);

            settings.Prompt = Prompt;
            settings.NegativePrompt = NegativePrompt ?? "";
            settings.Width = (uint)Width;
            settings.Height = (uint)Height;
            settings.ModelName = Model ?? "yiffymix_v52XL";
            settings.CFGScale = 4.5M;

            // Optionally upscale for premium users
            if (user.CheckAccessLevel(AccessLevel.PREMIUM))
            {
                settings.hires_enabled = true;
                settings.hires_denoising_strength = 0.4M;
                settings.hires_width = (uint)Math.Min(Width * 2, 1024);
                settings.hires_height = (uint)Math.Min(Height * 2, 1024);
                settings.hires_steps = 20;
            }

            // Generate the image
            var result = await FoxGenerate.Generate(t, settings, null, user);
        }
    }
}
