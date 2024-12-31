using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace makefoxsrv
{
    internal class FoxStrings
    {
        public static string SerializeToJson(Object any)
        {
            return JsonConvert.SerializeObject(any, Formatting.None, new JsonSerializerSettings
            {
                ReferenceLoopHandling = ReferenceLoopHandling.Ignore // Prevent loops during serialization
            });
        }

        public static bool TryParseDuration(string argument, out TimeSpan timeSpan)
        {
            timeSpan = TimeSpan.Zero;

            try
            {
                // Regex to handle formats like "1y5m1d4h33s", "1 day 45 minutes", "1d 4m", "1d4m", etc.
                var matches = Regex.Matches(argument, @"(\d+)\s*(y|years?|mo|months?|d|days?|h|hours?|m|minutes?|s|seconds?)", RegexOptions.IgnoreCase);

                foreach (Match match in matches)
                {
                    var value = int.Parse(match.Groups[1].Value);
                    var unit = match.Groups[2].Value.ToLower();

                    switch (unit)
                    {
                        case "y":
                        case "year":
                        case "years":
                            timeSpan += TimeSpan.FromDays(value * 365); // Approximate year as 365 days
                            break;
                        case "mo":
                        case "month":
                        case "months":
                            timeSpan += TimeSpan.FromDays(value * 30); // Approximate month as 30 days
                            break;
                        case "d":
                        case "day":
                        case "days":
                            timeSpan += TimeSpan.FromDays(value);
                            break;
                        case "h":
                        case "hour":
                        case "hours":
                            timeSpan += TimeSpan.FromHours(value);
                            break;
                        case "m":
                        case "minute":
                        case "minutes":
                            timeSpan += TimeSpan.FromMinutes(value);
                            break;
                        case "s":
                        case "second":
                        case "seconds":
                            timeSpan += TimeSpan.FromSeconds(value);
                            break;
                    }
                }

                return timeSpan > TimeSpan.Zero; // Ensure at least one valid match
            }
            catch
            {
                return false;
            }
        }


        public static string GenerateTestString(int length)
        {
            if (length < 0)
                throw new ArgumentException("Length must be non-negative.");

            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
            StringBuilder result = new StringBuilder(length);
            Random random = new Random();

            for (int i = 0; i < length; i++)
            {
                result.Append(chars[random.Next(chars.Length)]);
            }

            return result.ToString();
        }

        public static TimeSpan ParseTimeSpan(string timeSpan)
        {
            // Define a regex pattern to match the time span
            string pattern = @"^([+-]?\d+)\s*(\w+)$";
            var match = Regex.Match(timeSpan, pattern);

            if (!match.Success)
            {
                throw new ArgumentException("Invalid time span format.");
            }

            // Parse the number and the unit
            int value = int.Parse(match.Groups[1].Value);
            string unit = match.Groups[2].Value.ToLower();

            // Determine the TimeSpan adjustment
            switch (unit)
            {
                case "seconds":
                case "sec":
                case "s":
                    return TimeSpan.FromSeconds(value);

                case "minutes":
                case "min":
                case "m":
                    return TimeSpan.FromMinutes(value);

                case "hours":
                case "h":
                    return TimeSpan.FromHours(value);

                case "days":
                case "d":
                    return TimeSpan.FromDays(value);

                case "weeks":
                case "w":
                    return TimeSpan.FromDays(value * 7);

                default:
                    throw new ArgumentException($"Unknown time unit: {unit}");
            }
        }

        public static string[] text_help = {
@"Hi, I’m Makefoxbot. I can help you generate furry pictures through AI.

At the bottom left you’ll notice a blue menu button with various commands. Here’s what they do and the order in which you should use them to get started:

/setprompt followed by your prompt sets the image description. It’s generally good practice to use e621 tags separated by commas. You can also use parentheses for emphasis, e.g. (red ears:1.3) 

/setnegative sets your negative prompt, i.e. things you don’t want in your generation. It works the same as /setprompt. 

/setscale lets you define how closely the AI will follow your prompt. Default is at 7.5; generally you shouldn’t go above ~18 or you’ll get weird outputs.

/generate will then generate an image using the above input. If you’d like to skip the above you can also type /generate or /gen directly followed by your prompt.

If you prefer to use an input image with your prompt, just send me that image, define your prompt using /setprompt and /setnegative, then use /img2img to generate the output image.

/setdenoise lets you define how closely the AI will follow your input image. The default is at 0.75. 0 means the AI will copy the input image exactly, 1 means it will ignore it entirely.

All your settings and input images are stored by the bot until you replace them, so there is no need to input everything again for the same prompt. Either /generate or /img2img will work on their own.

Enjoy, and if you have any questions feel free to ask them in @toomanyfoxes

View a full list of commands: /commands",

@"/setprompt followed by your prompt sets the image description. It’s generally good practice to use e621 tags separated by commas, but other tags are also possible. You can also use parentheses for emphasis, e.g. (red ears:1.3), and you can group several traits within one pair of parentheses, which can be useful if you’re writing a prompt for something that involves multiple characters.

When using e621 tags, choose those that are both specific for what you want and reasonably frequent. The number will depend on specificity, but generally, a tag should have at least 200 occurrences on e621 for it to do you much good, ideally 1,000 and more. Replace underscores in tags with spaces and separate tags with commas.

The bot isn’t really built for free form/long sentence prompts, but occasionally they will work fine. 

Another thing to potentially include in your prompt are loras, which are specialized models that will improve outcomes for specific scenarios. The syntax for those is <lora:name:1>. A list of available loras is at xxx

Related commands: /setnegative, /setscale, /generate


/setnegative defines what you don’t want in your picture. It works the same way as /setprompt otherwise. In addition to specific tags you want to excuse, there are also some general models available that will prevent bad anatomy and other weird outcomes. Those are boring_e621_fluffyrock_v4, deformityv6, easynegative, bad anatomy, low quality.

BEWARE: If you put too much emphasis on a negative tag, it can sometimes have the opposite effect. Experiment and be aware that less may be more.

Related commands: /setprompt, /setscale


/setscale tells the AI how closely it should follow your text prompt. The default is at 7.5; lower values mean less weight, higher values mean more weight. This can be useful because the AI does not weigh all tags equally and sometimes you need to really push it to get a certain scenario, while in other cases it can be useful to make it a bit less eager to do so. Values above 18 are not recommended and will result in weird outcomes if chosen. 

Related commands: /setprompt, /setnegative",

@"/img2img allows you to generate a picture based on an input image and a text prompt. To use it, send me the input image, then set your prompt and negative prompt using /setprompt and /setnegative if you haven’t done so already. Following that, /img2img will then generate an image based on these inputs.

Related commands: /setdenoise, /select


/setdenoise lets you define how closely the AI will follow your input image. The default is at 0.75. 0 means the AI will copy the input image exactly, 1 means it will ignore it entirely. The best value will vary greatly depending on your prompt and input image, so experiment with this setting often.

This command only affects /img2img, it is not affected by /generate.

Related commands: /img2img, /select


/select turns your last output image into the input for your next img2img generation. This can be useful because you can approximate what you want through iterating img2img generations over multiple rounds of generations, discarding outputs that don’t show the desired outcome and keeping those that do.

Pushing the painter’s pallet button underneath any output image has the same effect and will select that image as the img2img input.

Related commands: /img2img, /setdenoise",

@"/setsize lets you define output image size in pixels. The default size is at 768x768. The maximum size you can request is at 1024x1024. Anything under 512x512 will generally result in low quality output.

If your input image isn’t a square, /img2img can result in distorted outputs, so it is best to adjust output image size to a similar proportion as your input image to avoid this effect. The input uses a widthxheight format.


/setseed lets you define the seed the AI uses to start generating the picture. If you don’t define it, the AI will choose it at random. The same input with the same seed and settings should always result in the same output. 

To return to random seed selection, use /setseed -1


/setsteps lets you define the number of steps the AI uses for generation. Generally, 15 to 20 steps is plenty; anything below or above will increase your chances of weird outputs."
        };

        public static string text_legal = @"
Legal:

This bot and the content generated are for research and educational purposes only.  For personal individual use only; do not sell generated content.  This system may generate harmful, incorrect or offensive content; you are responsible for following US law as well as your own local laws.";

    }
}
