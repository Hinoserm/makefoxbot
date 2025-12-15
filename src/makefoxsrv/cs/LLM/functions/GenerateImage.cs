using SixLabors.ImageSharp.Formats;
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
            [LLMParam("Quantity of images to generate (up to 3 for Premium or Admin users, limit 1 for free users).", false)] int Quantity = 1,
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

            string? loraString = LORAs is { Count: > 0 }
                ? string.Join("\r\n", LORAs.Select(name => $"<lora:{name}:1.0>"))
                : null;

            if (loraString is not null)
                settings.Prompt += "\r\n\r\n" + loraString;

            // We need to make sure Quantity doesn't exceed the user's allowed queue size

            bool isPremium = user.CheckAccessLevel(AccessLevel.PREMIUM) || await FoxGroupAdmin.CheckGroupIsPremium(t.Chat);

            if (user.GetAccessLevel() < AccessLevel.ADMIN)
            {
                int userQueueLimit = isPremium ? 3 : 1;
                int userQueueCount = await FoxQueue.GetCount(user);

                int remainingQuantityAvailable = userQueueLimit - userQueueCount;

                if (remainingQuantityAvailable <= 0)
                {
                    throw new Exception($"Queued image limit of {userQueueLimit} tasks reached. Instruct user to try again shortly.");
                }

                Quantity = Math.Min(Quantity, remainingQuantityAvailable);
            }

            // Generate the image(s)
            for (int i = 0; i < Quantity; i++)
            {
                var result = await FoxGenerate.Generate(t, settings, null, user);
            }
        }
    }
}
