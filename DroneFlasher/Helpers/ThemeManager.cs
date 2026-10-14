using DroneFlasher.Views;
using System.Windows;

namespace DroneFlasher.Helpers

{
    public static class ThemeManager
    {
        private const string ThemeFolder = "/Themes/";

        public static string CurrentThemeKey { get; private set; }
            = "Dark";

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

        public static void ApplyTheme(
            MainWindow window,
            string themeKey)
        {
            if (window == null)
                throw new ArgumentNullException(nameof(window));

            if (string.IsNullOrWhiteSpace(themeKey))
                throw new ArgumentException(
                    "The theme name is not specified",
                    nameof(themeKey));

            if (!AllowedThemes.Contains(
                    themeKey,
                    StringComparer.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    $"Unknown theme: {themeKey}",
                    nameof(themeKey));
            }

            themeKey = AllowedThemes.First(
                t => string.Equals(
                    t,
                    themeKey,
                    StringComparison.OrdinalIgnoreCase));

            var newTheme = new ResourceDictionary
            {
                Source = new Uri(
                    $"{ThemeFolder}{themeKey}.xaml",
                    UriKind.Relative)
            };

            _ = newTheme.Count;

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

        public static bool IsThemeAvailable(string themeKey)
        {
            return !string.IsNullOrWhiteSpace(themeKey) &&
                   AllowedThemes.Contains(
                       themeKey,
                       StringComparer.OrdinalIgnoreCase);
        }

        public static string[] GetAvailableThemes()
        {
            return (string[])AllowedThemes.Clone();
        }
    }
}