using SmartFormat;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Resources;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;

namespace makefoxsrv
{
    public class FoxLocalization
    {
        private readonly FoxUser user;
        private static ResourceManager resourceManager = new ResourceManager("makefoxsrv.lang.Strings", Assembly.GetExecutingAssembly());
        private static readonly SmartFormatter formatter = Smart.CreateDefaultSmartFormat();

        public static Dictionary<string, (string CultureInfoName, string EmojiFlag)> languageLookup { get; private set; } = new Dictionary<string, (string CultureInfoName, string EmojiFlag)>()
        {
            {"en", ("en-US", "🇺🇸")},
            {"cs", ("cs-CZ", "🇨🇿")},
            {"de", ("de-DE", "🇩🇪")},
            {"nl", ("nl-NL", "🇳🇱")},
            {"pl", ("pl-PL", "🇵🇱")},
            {"es", ("es-ES", "🇪🇸")},
            {"pt-br", ("pt-BR", "🇧🇷")},
            {"sl", ("sl-SI", "🇸🇮")},
            {"ru", ("ru-RU", "🇷🇺")},
            {"id", ("id-ID", "🇮🇩")},
            {"it", ("it-IT", "🇮🇹")},
            {"ar", ("ar-SA", "🇸🇦")},
            {"uk", ("uk-UA", "🇺🇦")},
            {"fr", ("fr-FR", "🇫🇷")},
            {"tr", ("tr-TR", "🇹🇷")},
            {"fi", ("fi-FI", "🇫🇮")},
            {"ro", ("ro-RO", "🇷🇴")},
            {"sk", ("sk-SK", "🇸🇰")},
            {"vi", ("vi-VN", "🇻🇳")},
            {"el", ("el-GR", "🇬🇷")}
        };

        public string emojiFlag { get; private set; }
        public string localeName { get; private set; }

        public CultureInfo localeCulture { get; private set; }

        public FoxLocalization(FoxUser user, string language)
        {
            var lowerLanguage = language.ToLower();
            if (!languageLookup.ContainsKey(lowerLanguage))
                throw new Exception($"Language '{language}' not supported.");

            this.user = user;
            this.localeCulture = new CultureInfo(languageLookup[lowerLanguage].CultureInfoName);
            this.localeName = languageLookup[lowerLanguage].CultureInfoName;
            this.emojiFlag = languageLookup[lowerLanguage].EmojiFlag;
        }

        private static readonly ResourceManager ResourceManager =
        new ResourceManager("makefoxsrv.lang.Strings", Assembly.GetExecutingAssembly());

        static FoxLocalization()
        {
            var assembly = Assembly.GetExecutingAssembly();
            var allResourceNames = assembly.GetManifestResourceNames();
            
            FilterSupportedCultures();
        }

        public static void FilterSupportedCultures()
        {
            var allCultures = CultureInfo.GetCultures(CultureTypes.AllCultures);
            var supportedCultures = new List<string>();

            // Filter the languageLookup based on available resource files

            foreach (var culture in allCultures)
            {
                try
                {
                    var resourceSet = ResourceManager.GetResourceSet(culture, true, false);
                    if (resourceSet != null && !string.IsNullOrEmpty(culture.Name) && culture.Name != "en-US")
                    {
                        // Add the culture.Name to supportedCultures if a ResourceSet is found
                        supportedCultures.Add(culture.Name);
                    }
                }
                catch (CultureNotFoundException)
                {
                    // This culture is not supported or does not have resources, ignore it
                }
            }

            // Assuming "en" is always supported as it's the default and may not have a separate ResourceSet
            supportedCultures.Add("en-US");

            // Filter the languageLookup to include only those cultures found in supportedCultures
            languageLookup = languageLookup
                .Where(l => supportedCultures.Contains(l.Value.CultureInfoName))
                .ToDictionary(l => l.Key, l => l.Value);
        }

        public TL.ReplyInlineMarkup GenerateLanguageButtons()
        {
            var buttonRows = new List<TL.KeyboardButtonRow>();

            // Temporary list to accumulate buttons for the current row
            var currentRowButtons = new List<TL.KeyboardButtonBase>();

            int maxButtonsPerRow = 4; // Adjust as necessary

            foreach (var language in languageLookup)
            {
                if (language.Value.CultureInfoName == localeName)
                    continue; // Skip the currently selected language

                var buttonText = $"{language.Value.EmojiFlag} {language.Key.ToUpper()}";
                var buttonData = Encoding.ASCII.GetBytes($"/lang {language.Key}");

                // Add new button to the current row's list
                currentRowButtons.Add(new TL.KeyboardButtonCallback { text = buttonText, data = buttonData });

                // If the current row is full, add it to buttonRows and start a new row
                if (currentRowButtons.Count >= maxButtonsPerRow)
                {
                    buttonRows.Add(new TL.KeyboardButtonRow { buttons = currentRowButtons.ToArray() });
                    currentRowButtons = new List<TL.KeyboardButtonBase>(); // Reset the list for the next row
                }
            }

            // Add any remaining buttons to the last row
            if (currentRowButtons.Count > 0)
            {
                buttonRows.Add(new TL.KeyboardButtonRow { buttons = currentRowButtons.ToArray() });
            }

            var inlineKeyboardButtons = new TL.ReplyInlineMarkup
            {
                rows = buttonRows.ToArray()
            };

            return inlineKeyboardButtons;
        }


        public string Get(string key)
        {
            // Fetches the string using the user's preferred language
            var text = resourceManager.GetString(key, localeCulture);

            if (text is null)
                throw new Exception($"Localization key '{key}' not found for language '{localeName}'.");

            return text;
        }

        // Named args via anonymous or typed object
        public string Get(string key, object? templateData)
        {
            if (key is null)
                throw new ArgumentNullException(nameof(key));

            var format = resourceManager.GetString(key, localeCulture);

            if (format is null)
                throw new Exception($"Localization key '{key}' not found for language '{localeName}'.");

            if (templateData is null)
                return format;

            return formatter.Format(format, templateData);
        }

        // Optional: named args via dictionary
        public string Get(string key, IReadOnlyDictionary<string, object?> values)
        {
            if (key is null)
                throw new ArgumentNullException(nameof(key));
            if (values is null)
                throw new ArgumentNullException(nameof(values));

            var format = resourceManager.GetString(key, localeCulture);

            if (format is null)
                throw new Exception($"Localization key '{key}' not found for language '{localeName}'.");

            return formatter.Format(format, values);
        }
    }
}
