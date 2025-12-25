using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Threading;

namespace L1MapViewer.Localization
{
    /// <summary>
    /// Manages application localization and language switching
    /// </summary>
    public static class LocalizationManager
    {
        // Supported languages
        public static readonly Dictionary<string, string> SupportedLanguages = new()
        {
            { "zh-TW", "繁體中文" },
            { "ja-JP", "日本語" },
            { "ko-KR", "한국어" },
            { "en-US", "English" }
        };

        // Default language
        public const string DefaultLanguage = "zh-TW";

        // Current culture
        private static CultureInfo _currentCulture;

        // String resources
        private static Dictionary<string, Dictionary<string, string>> _resources;

        // Event fired when language changes
        public static event EventHandler LanguageChanged;

        /// <summary>
        /// Current language code (e.g., "zh-TW", "ja-JP", "en-US")
        /// </summary>
        public static string CurrentLanguage => _currentCulture?.Name ?? DefaultLanguage;

        /// <summary>
        /// Initialize the localization manager
        /// </summary>
        public static void Initialize()
        {
            // Load all string resources
            LoadResources();

            // Load saved language or detect system language
            string savedLanguage = LoadSavedLanguage();
            string languageToUse = savedLanguage ?? DetectSystemLanguage();

            SetLanguage(languageToUse, savePreference: false);
        }

        /// <summary>
        /// Load string resources from embedded data
        /// </summary>
        private static void LoadResources()
        {
            _resources = new Dictionary<string, Dictionary<string, string>>();

            // Load zh-TW (Traditional Chinese - Default)
            _resources["zh-TW"] = Strings_zhTW.GetStrings();

            // Load ja-JP (Japanese)
            _resources["ja-JP"] = Strings_jaJP.GetStrings();

            // Load ko-KR (Korean)
            _resources["ko-KR"] = Strings_koKR.GetStrings();

            // Load en-US (English)
            _resources["en-US"] = Strings_enUS.GetStrings();
        }

        /// <summary>
        /// Detect system language and map to supported language
        /// </summary>
        private static string DetectSystemLanguage()
        {
            string systemLanguage = CultureInfo.CurrentUICulture.Name;

            // Exact match
            if (SupportedLanguages.ContainsKey(systemLanguage))
                return systemLanguage;

            // Partial match (e.g., "zh-CN" -> "zh-TW", "ja" -> "ja-JP")
            string twoLetterCode = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;

            if (twoLetterCode == "zh")
                return "zh-TW";
            if (twoLetterCode == "ja")
                return "ja-JP";
            if (twoLetterCode == "ko")
                return "ko-KR";

            // Default to English for unsupported languages
            return "en-US";
        }

        /// <summary>
        /// Set the application language
        /// </summary>
        /// <param name="languageCode">Language code (e.g., "zh-TW")</param>
        /// <param name="savePreference">Whether to save this as user preference</param>
        public static void SetLanguage(string languageCode, bool savePreference = true)
        {
            if (!SupportedLanguages.ContainsKey(languageCode))
                languageCode = DefaultLanguage;

            _currentCulture = new CultureInfo(languageCode);

            // Set thread culture for WinForms
            Thread.CurrentThread.CurrentUICulture = _currentCulture;
            Thread.CurrentThread.CurrentCulture = _currentCulture;

            // Save preference
            if (savePreference)
                SaveLanguagePreference(languageCode);

            // Fire language changed event
            LanguageChanged?.Invoke(null, EventArgs.Empty);
        }

        /// <summary>
        /// Get localized string by key
        /// </summary>
        /// <param name="key">Resource key</param>
        /// <returns>Localized string or key if not found</returns>
        public static string GetString(string key)
        {
            if (_resources == null)
                Initialize();

            string lang = CurrentLanguage;

            // Try current language
            if (_resources.TryGetValue(lang, out var langStrings))
            {
                if (langStrings.TryGetValue(key, out var value))
                    return value;
            }

            // Fallback to default language
            if (lang != DefaultLanguage && _resources.TryGetValue(DefaultLanguage, out var defaultStrings))
            {
                if (defaultStrings.TryGetValue(key, out var value))
                    return value;
            }

            // Return key if not found
            return key;
        }

        /// <summary>
        /// Get localized string with format parameters
        /// </summary>
        public static string GetString(string key, params object[] args)
        {
            string format = GetString(key);
            try
            {
                return string.Format(format, args);
            }
            catch
            {
                return format;
            }
        }

        /// <summary>
        /// Shorthand for GetString
        /// </summary>
        public static string L(string key) => GetString(key);

        /// <summary>
        /// Shorthand for GetString with format
        /// </summary>
        public static string L(string key, params object[] args) => GetString(key, args);

        // --- Persistence Methods ---

        private static readonly string SettingsPath =
            Path.Combine(Path.GetTempPath(), "mapviewer_settings.json");

        private static string LoadSavedLanguage()
        {
            try
            {
                if (File.Exists(SettingsPath))
                {
                    var json = File.ReadAllText(SettingsPath);
                    var settings = JsonSerializer.Deserialize<AppSettings>(json);
                    if (settings?.Language != null && SupportedLanguages.ContainsKey(settings.Language))
                        return settings.Language;
                }
            }
            catch { }
            return null;
        }

        private static void SaveLanguagePreference(string languageCode)
        {
            try
            {
                var settings = LoadSettings() ?? new AppSettings();
                settings.Language = languageCode;

                var json = JsonSerializer.Serialize(settings,
                    new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(SettingsPath, json);
            }
            catch { }
        }

        private static AppSettings LoadSettings()
        {
            try
            {
                if (File.Exists(SettingsPath))
                {
                    var json = File.ReadAllText(SettingsPath);
                    return JsonSerializer.Deserialize<AppSettings>(json);
                }
            }
            catch { }
            return null;
        }
    }

    /// <summary>
    /// Application settings model
    /// </summary>
    internal class AppSettings
    {
        public string Language { get; set; }
    }
}
