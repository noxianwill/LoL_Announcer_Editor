using System;
using System.IO;
using System.Text.Json;

namespace LoL_Announcer_Editor
{
    public static class SettingsManager
    {
        // Settings properties with updated default values
        public static bool PlayOnClick { get; set; } = true;
        public static bool ExtractOgg { get; set; } = false;
        
        // Appearance Settings
        public static AppTheme CurrentTheme { get; set; } = AppTheme.Windows11;
        public static string AccentColorHex { get; set; } = "#C89B3C"; // Default Hextech Gold

        // Settings file path (now in the same folder as the .exe)
        private static string SettingsFilePath => Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "settings.json"
        );

        // Settings data structure for serialization
        private class SettingsData
        {
            public bool PlayOnClick { get; set; }
            public bool ExtractOgg { get; set; }
            public AppTheme CurrentTheme { get; set; }
            public string AccentColorHex { get; set; }
        }

        // Load settings from file
        public static void LoadSettings()
        {
            try
            {
                if (File.Exists(SettingsFilePath))
                {
                    string json = File.ReadAllText(SettingsFilePath);
                    var settings = JsonSerializer.Deserialize<SettingsData>(json);

                    if (settings != null)
                    {
                        PlayOnClick = settings.PlayOnClick;
                        ExtractOgg = settings.ExtractOgg;
                        CurrentTheme = settings.CurrentTheme;
                        AccentColorHex = settings.AccentColorHex ?? "#C89B3C";
                    }
                }
            }
            catch
            {
                // Keep default values on error
            }
        }

        // Save settings to file
        public static void SaveSettings()
        {
            try
            {
                string? directory = Path.GetDirectoryName(SettingsFilePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var settings = new SettingsData
                {
                    PlayOnClick = PlayOnClick,
                    ExtractOgg = ExtractOgg,
                    CurrentTheme = CurrentTheme,
                    AccentColorHex = AccentColorHex
                };

                string json = JsonSerializer.Serialize(settings, new JsonSerializerOptions
                {
                    WriteIndented = true
                });
                File.WriteAllText(SettingsFilePath, json);
            }
            catch
            {
            }
        }

        private static SettingsData? _backupSettings;

        public static void BackupSettings()
        {
            _backupSettings = new SettingsData
            {
                PlayOnClick = PlayOnClick,
                ExtractOgg = ExtractOgg,
                CurrentTheme = CurrentTheme,
                AccentColorHex = AccentColorHex
            };
        }

        public static void RestoreSettings()
        {
            if (_backupSettings != null)
            {
                PlayOnClick = _backupSettings.PlayOnClick;
                ExtractOgg = _backupSettings.ExtractOgg;
                CurrentTheme = _backupSettings.CurrentTheme;
                AccentColorHex = _backupSettings.AccentColorHex;
            }
        }

        // Reset to defaults
        public static void ResetToDefaults()
        {
            PlayOnClick = true;
            ExtractOgg = false;
            CurrentTheme = AppTheme.Windows11;
            AccentColorHex = "#C89B3C";
        }
    }
}
