using System;
using System.IO;
using System.Text.Json;

namespace LoL_Announcer_Editor
{
    public static class SettingsManager
    {
        // Settings properties with updated default values
        public static bool PlayOnClick { get; set; } = true;        // Default: TRUE
        public static bool ExtractOgg { get; set; } = false;        // Default: FALSE
        // No AllowMultiselect property or logic

        // Settings file path
        private static string SettingsFilePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "LoL Announcer Manager",
            "settings.json"
        );

        // Settings data structure for serialization
        private class SettingsData
        {
            public bool PlayOnClick { get; set; }
            public bool ExtractOgg { get; set; }
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
                    }
                    
                    Defs.LogAction($"Settings loaded from {SettingsFilePath}");
                }
                else
                {
                    Defs.LogAction("Settings file not found, using default values");
                }
            }
            catch (Exception ex)
            {
                Defs.LogError($"Failed to load settings: {ex.Message}");
                // Keep default values on error
            }
        }

        // Save settings to file
        public static void SaveSettings()
        {
            try
            {
                // Ensure directory exists
                string? directory = Path.GetDirectoryName(SettingsFilePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var settings = new SettingsData
                {
                    PlayOnClick = PlayOnClick,
                    ExtractOgg = ExtractOgg
                };

                string json = JsonSerializer.Serialize(settings, new JsonSerializerOptions 
                { 
                    WriteIndented = true 
                });
                File.WriteAllText(SettingsFilePath, json);
                
                Defs.LogAction($"Settings saved to {SettingsFilePath}");
            }
            catch (Exception ex)
            {
                Defs.LogError($"Failed to save settings: {ex.Message}");
            }
        }

        // Backup current settings (for cancel functionality)
        private static SettingsData? _backupSettings;

        public static void BackupSettings()
        {
            _backupSettings = new SettingsData
            {
                PlayOnClick = PlayOnClick,
                ExtractOgg = ExtractOgg
            };
            Defs.LogAction("Settings backed up");
        }

        public static void RestoreSettings()
        {
            if (_backupSettings != null)
            {
                PlayOnClick = _backupSettings.PlayOnClick;
                ExtractOgg = _backupSettings.ExtractOgg;
                Defs.LogAction("Settings restored from backup");
            }
        }

        // Reset to defaults
        public static void ResetToDefaults()
        {
            PlayOnClick = true;
            ExtractOgg = false;
            Defs.LogAction("Settings reset to defaults");
        }
    }
} 