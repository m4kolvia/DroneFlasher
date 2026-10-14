using DroneFlasher.Helpers;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace DroneFlasher.Views
{
    public partial class ThemeSettingsWindow : Window
    {
        private readonly ObservableCollection<ThemeOption> _themes;
        private readonly MainWindow _mainWindow;

        public ThemeSettingsWindow(MainWindow mainWindow)
        {
            InitializeComponent();

            _mainWindow = mainWindow;
            Owner = mainWindow;

            Resources.MergedDictionaries.Add(_mainWindow.Resources);

            _themes = new ObservableCollection<ThemeOption>
            {
                new("Dark", "Темна тема", "#111114", "Dark"),
                new("Light", "Світле оформлення", "#2563EB", "Light"),
                new("Sunset", "Захід сонця", "#FF8A4C", "Sunset"),
                new("Midnight Blue", "Глибокий синій", "#2563EB", "MidnightBlue"),
                new("Arctic", "Холодний блакитний", "#38BDF8", "Arctic"),
                new("Forest", "Зелений ліс", "#22C55E", "Forest"),
                new("Amethyst", "Фіолетовий", "#A78BFA", "Amethyst"),
                new("Crimson", "Насичений червоний", "#F43F5E", "Crimson"),
                new("Amber", "Теплий бурштиновий", "#F59E0B", "Amber"),
                new("Graphite", "Стриманий графіт", "#9CA3AF", "Graphite"),
                new("Cyber Teal", "Кібер-бірюзовий", "#2DD4BF", "CyberTeal"),
                new("Nebula", "Космічний пурпурний", "#C084FC", "Nebula"),
            };

            ThemesList.ItemsSource = _themes;

            MarkSelectedTheme(ThemeManager.CurrentThemeKey);
        }

        private void ThemeButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button ||
                button.Tag is not string themeKey)
            {
                return;
            }

            try
            {
                ThemeManager.ApplyTheme(_mainWindow, themeKey);

                MarkSelectedTheme(themeKey);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    this,
                    $"Не вдалося застосувати тему.\n\n{ex.Message}",
                    "Помилка зміни теми",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void MarkSelectedTheme(string themeKey)
        {
            foreach (var theme in _themes)
            {
                theme.IsSelected = theme.Key == themeKey;
            }

            var selected = _themes.FirstOrDefault(
                theme => theme.Key == themeKey);
        }

        private void TitleBar_MouseLeftButtonDown(
            object sender,
            MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
                DragMove();
        }

        private void CloseButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            Close();
        }
    }

    public sealed class ThemeOption : System.ComponentModel.INotifyPropertyChanged
    {
        private bool _isSelected;

        public string Name { get; }
        public string Description { get; }
        public string Accent { get; }
        public string Key { get; }

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value)
                    return;

                _isSelected = value;
                PropertyChanged?.Invoke(
                    this,
                    new System.ComponentModel.PropertyChangedEventArgs(
                        nameof(IsSelected)));
            }
        }

        public event System.ComponentModel.PropertyChangedEventHandler?
            PropertyChanged;

        public ThemeOption(
            string name,
            string description,
            string accent,
            string key)
        {
            Name = name;
            Description = description;
            Accent = accent;
            Key = key;
        }
    }
}