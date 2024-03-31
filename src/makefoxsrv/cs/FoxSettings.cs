#nullable enable

using MySqlConnector;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using WTelegram;
using makefoxsrv;

//This deals with getting default values and other settings from the DB.
// Some defaults are hard-coded here.

namespace makefoxsrv
{
    internal class FoxSettings
    {

        private static Dictionary<string, object> _settings = new Dictionary<string, object>();

        // Hardcoded default values
        private static readonly Dictionary<string, object> _defaultSettings = new Dictionary<string, object>
        {
            {"DefaultSteps",    20},       // Default steps setting for users
            {"DefaultWidth",    640},      // Default image width for users
            {"DefaultHeight",   768},      // Default image height for users
            {"DefaultDenoise",  0.75M},    // Default denoising strength for users
            {"DefaultCFGScale", 7.5M},     // Default CFG scale setting for users
            {"DefaultModel",    "indigoFurryMix_v105Hybrid" },
            {"GetFullUser",     false },
            {"GetUserPhoto",    false },
            {"GetFullChat",     true },
            {"GetChatPhoto",    false },
            {"GetChatAdmins",   false },
        };

        private static object ConvertToType(string key, string value, Type type)
        {
            object? result = Type.GetTypeCode(type) switch
            {
                TypeCode.Int32 when int.TryParse(value, out var intResult) => intResult,
                TypeCode.Decimal when decimal.TryParse(value, out var decimalResult) => decimalResult,
                TypeCode.Boolean when bool.TryParse(value, out var boolResult) => boolResult,
                TypeCode.String => value,
                _ => null, // Use null as a marker for unsupported or failed conversions
            };

            // Check if conversion was successful (result is not null) or if the type was string (always successful)
            if (result is not null || type == typeof(string))
            {
                return result!;
            }
            else if (type == typeof(bool))
            {
                if (int.TryParse(value, out var intValue))
                {
                    return intValue != 0;
                }
                else if (bool.TryParse(value, out var boolValue))
                {
                    return boolValue;
                }
                else if (value.Equals("yes", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
                else if (value.Equals("no", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            throw new ArgumentException($"Incompatible conversion: Unable to convert {key}='{value}' to {type}.");
        }

        // Load or reload the default settings from the database asynchronously
        public static async Task LoadSettingsAsync()
        {
            // Start with hardcoded defaults
            var newSettings = new Dictionary<string, object>(_defaultSettings);

            try
            {
                using var SQL = new MySqlConnection(FoxMain.MySqlConnectionString);

                await SQL.OpenAsync();

                using (var cmd = new MySqlCommand("SELECT * FROM settings", SQL))
                {
                    using var reader = await cmd.ExecuteReaderAsync();

                    while (await reader.ReadAsync())
                    {
                        string db_key = reader.GetString("key");
                        string db_value = reader.GetString("value");

                        if (_defaultSettings.TryGetValue(db_key, out var defaultValue))
                        {
                            newSettings[db_key] = ConvertToType(db_key, db_value, defaultValue.GetType());
                        }
                        else
                        {
                            newSettings[db_key] = db_value;
                        }
                    }
                }

                _settings = newSettings; //Only save if everything was successful.
            }
            catch (Exception ex)
            {
                // Consider logging the error
                throw;
            }
        }

        public static T Get<T>(string settingName)
        {
            if (_settings.TryGetValue(settingName, out var value))
            {
                // If the value is already of type T, return it directly
                if (value is T variable)
                    return variable;

                try
                {
                    // Attempt to convert the value to the requested type T
                    Type targetType = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);

                    if (targetType != typeof(string) && value is string stringValue)
                        return (T)ConvertToType(settingName, stringValue, targetType);

                    return (T)Convert.ChangeType(value, targetType);
                }
                catch
                {
                    throw new InvalidOperationException($"Could not convert setting '{settingName}' to type {typeof(T)}. Value: {value}");
                }
            }
            else
            {
                // For non-existing settings:
                // If T is a nullable type, return null; otherwise, throw an exception.
                if (Nullable.GetUnderlyingType(typeof(T)) != null || !typeof(T).IsValueType)
                {
                    return default(T); // Returns null for nullable types
                }
                else
                {
                    throw new KeyNotFoundException($"Default value for setting '{settingName}' not found.");
                }
            }
        }
    }
}
