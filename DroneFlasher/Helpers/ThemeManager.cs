using System;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DroneFlasher.Helpers
{
    public static class ThemeManager
    {
        private const string ThemeFolder = "/Themes/";

        private static readonly string SettingsFolder =
            Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.ApplicationData),
                "DroneFlasher");

        private static readonly string SettingsPath =
            Path.Combine(SettingsFolder, "theme-settings.json");

        public static string CurrentThemeKey { get; private set; } = "Dark";

        public static string? CurrentBackgroundImage { get; private set; }

        public static double CurrentBackgroundOpacity { get; private set; } = 0.35;

        private static readonly string[] AllowedThemes =
        {
            "MidnightBlue",
            "Arctic",
            "Forest",
            "Amethyst",
            "Crimson",
            "Amber",
            "Graphite",
            "CyberTeal",
            "Light",
            "Nebula",
            "Dark",
            "Sunset"
        };

        private sealed class ThemeSettings
        {
            public string ThemeKey { get; set; } = "Dark";
            public string? BackgroundImagePath { get; set; }
            public double BackgroundOpacity { get; set; } = 0.35;
        }

        public static void LoadSettings(Window window)
        {
            if (window == null)
                throw new ArgumentNullException(nameof(window));

            string themeKey = "Dark";
            string? backgroundImage = null;

            try
            {
                if (File.Exists(SettingsPath))
                {
                    string json = File.ReadAllText(SettingsPath);
                    var settings = JsonSerializer.Deserialize<ThemeSettings>(json);

                    if (settings != null)
                    {
                        CurrentBackgroundOpacity = settings.BackgroundOpacity;

                        if (IsThemeAvailable(settings.ThemeKey))
                            themeKey = settings.ThemeKey;

                        if (!string.IsNullOrWhiteSpace(settings.BackgroundImagePath) &&
                            File.Exists(settings.BackgroundImagePath))
                        {
                            backgroundImage = settings.BackgroundImagePath;
                        }
                    }
                }
            }
            catch
            {
                themeKey = "Dark";
                backgroundImage = null;
            }

            ApplyThemePaletteOnly(window, themeKey);

            if (!string.IsNullOrWhiteSpace(backgroundImage))
            {
                SetBackgroundImage(window, backgroundImage);
            }
            else
            {
                CurrentBackgroundImage = null;
                ApplyBackgroundState(window);
            }
        }

        public static void ApplyTheme(Window window, string themeKey)
        {
            ApplyThemePaletteOnly(window, themeKey);
            SaveSettings();

            if (!string.IsNullOrWhiteSpace(CurrentBackgroundImage))
            {
                SetBackgroundImage(window, CurrentBackgroundImage);
            }
            else
            {
                ApplyBackgroundState(window);
            }
        }

        private static void ApplyThemePaletteOnly(Window window, string themeKey)
        {
            if (window == null)
                throw new ArgumentNullException(nameof(window));

            if (string.IsNullOrWhiteSpace(themeKey))
            {
                throw new ArgumentException("The theme name is not specified", nameof(themeKey));
            }

            themeKey =
                AllowedThemes.FirstOrDefault(t =>
                    string.Equals(t, themeKey, StringComparison.OrdinalIgnoreCase))
                ?? throw new ArgumentException($"Unknown theme: {themeKey}", nameof(themeKey));

            var newTheme = new ResourceDictionary
            {
                Source = new Uri(
                    $"{ThemeFolder}{themeKey}.xaml",
                    UriKind.Relative)
            };

            var dictionaries = window.Resources.MergedDictionaries;

            var oldThemes = dictionaries
                .Where(dictionary =>
                    dictionary.Source != null &&
                    dictionary.Source.OriginalString.StartsWith(
                        ThemeFolder,
                        StringComparison.OrdinalIgnoreCase) &&
                    !dictionary.Source.OriginalString.EndsWith(
                        "/BasePalette.xaml",
                        StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var oldTheme in oldThemes)
            {
                dictionaries.Remove(oldTheme);
            }

            dictionaries.Add(newTheme);
            CurrentThemeKey = themeKey;
        }

        public static void SetBackgroundImage(Window window, string? imagePath, double? opacity = null)
        {
            if (window == null)
                return;

            if (opacity.HasValue)
            {
                CurrentBackgroundOpacity = Math.Clamp(opacity.Value, 0, 1.0);
            }

            if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
            {
                CurrentBackgroundImage = null;
                ApplyBackgroundState(window);
                SaveSettings();
                return;
            }

            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.UriSource = new Uri(Path.GetFullPath(imagePath), UriKind.Absolute);
                bitmap.EndInit();
                bitmap.Freeze();

                var imageBrush = new ImageBrush(bitmap)
                {
                    Stretch = Stretch.UniformToFill,
                    Opacity = CurrentBackgroundOpacity 
                };

                window.Background = imageBrush;
                CurrentBackgroundImage = Path.GetFullPath(imagePath);

                SaveSettings();
            }
            catch (Exception)
            {
                CurrentBackgroundImage = null;
                ApplyBackgroundState(window);
                SaveSettings();
            }
        }

        private static void ApplyBackgroundState(Window window)
        {
            if (!string.IsNullOrWhiteSpace(CurrentBackgroundImage))
            {
                return;
            }

            if (window.Resources["WindowBackgroundBrush"] is Brush themeBrush)
            {
                window.Background = themeBrush;
            }
            else
            {
                window.SetResourceReference(Window.BackgroundProperty, "WindowBackgroundBrush");
            }
        }

        private static void SaveSettings()
        {
            try
            {
                Directory.CreateDirectory(SettingsFolder);

                var settings = new ThemeSettings
                {
                    ThemeKey = CurrentThemeKey,
                    BackgroundImagePath = CurrentBackgroundImage,
                    BackgroundOpacity = CurrentBackgroundOpacity
                };

                string json = JsonSerializer.Serialize(
                    settings,
                    new JsonSerializerOptions
                    {
                        WriteIndented = true
                    });

                File.WriteAllText(SettingsPath, json);
            }
            catch
            {
            }
        }

        public static bool IsThemeAvailable(string themeKey)
        {
            return !string.IsNullOrWhiteSpace(themeKey) &&
                   AllowedThemes.Contains(themeKey, StringComparer.OrdinalIgnoreCase);
        }

        public static string[] GetAvailableThemes()
        {
            return (string[])AllowedThemes.Clone();
        }
    }
}