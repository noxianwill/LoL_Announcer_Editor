using System;
using System.Linq;
using System.Windows;
using System.Windows.Media;

using LoL_Announcer_Editor.Models;

namespace LoL_Announcer_Editor.Services
{
    /// <summary>
    /// Service for managing application themes and accent colors.
    /// Handles runtime resource dictionary switching.
    /// </summary>
    public static class ThemeManager
    {
        private static ResourceDictionary? _currentThemeDictionary;
        private static ResourceDictionary _accentDictionary = new ResourceDictionary();

        static ThemeManager()
        {
        }

        public static void ApplySavedTheme()
        {
            SetTheme(SettingsManager.CurrentTheme);
            SetAccentColor((Color)ColorConverter.ConvertFromString(SettingsManager.AccentColorHex));
        }

        public static void SetTheme(AppTheme theme)
        {
            SettingsManager.CurrentTheme = theme;
            SettingsManager.SaveSettings();

            var dict = new ResourceDictionary();
            string themePath = theme switch
            {
                AppTheme.Windows11 => "Themes/Windows11.xaml",
                AppTheme.Windows10 => "Themes/Windows10.xaml",
                AppTheme.Hextech => "Themes/Hextech.xaml",
                AppTheme.Light => "Themes/Light.xaml",
                _ => "Themes/Windows11.xaml"
            };

            try
            {
                dict.Source = new Uri(themePath, UriKind.Relative);
            }
            catch
            {
                return;
            }

            if (_currentThemeDictionary != null)
            {
                Application.Current.Resources.MergedDictionaries.Remove(_currentThemeDictionary);
            }

            _currentThemeDictionary = dict;
            Application.Current.Resources.MergedDictionaries.Add(_currentThemeDictionary);
            
            if (!Application.Current.Resources.MergedDictionaries.Contains(_accentDictionary))
            {
                Application.Current.Resources.MergedDictionaries.Add(_accentDictionary);
            }
            else
            {
                Application.Current.Resources.MergedDictionaries.Remove(_accentDictionary);
                Application.Current.Resources.MergedDictionaries.Add(_accentDictionary);
            }
        }

        public static void SetAccentColor(Color color)
        {
            SettingsManager.AccentColorHex = color.ToString();
            SettingsManager.SaveSettings();

            _accentDictionary["AccentColor"] = color;
            _accentDictionary["AccentBrush"] = new SolidColorBrush(color);
            
            var hoverColor = Color.FromArgb(200, color.R, color.G, color.B);
            _accentDictionary["AccentHoverBrush"] = new SolidColorBrush(hoverColor);
            
            var pressedColor = Color.FromArgb(150, color.R, color.G, color.B);
            _accentDictionary["AccentPressedBrush"] = new SolidColorBrush(pressedColor);
        }

        public static void ResetAppearance()
        {
            SettingsManager.ResetToDefaults();
            ApplySavedTheme();
        }
    }
}
