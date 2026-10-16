using DroneFlasher.Helpers;
using DroneFlasher.Models;
using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace DroneFlasher.Views
{
    public partial class ThemeSettingsWindow : Window, INotifyPropertyChanged
    {
        private readonly ObservableCollection<ThemeOption> _themes;
        private readonly MainWindow _mainWindow;

        private bool _hasBackgroundImage;
        private string _backgroundImagePath = string.Empty;
        private string _backgroundImageName = string.Empty;

        public bool HasBackgroundImage
        {
            get => _hasBackgroundImage;
            private set
            {
                if (_hasBackgroundImage == value)
                    return;

                _hasBackgroundImage = value;
                OnPropertyChanged(nameof(HasBackgroundImage));
            }
        }

        public string BackgroundImagePath
        {
            get => _backgroundImagePath;
            private set
            {
                if (_backgroundImagePath == value)
                    return;

                _backgroundImagePath = value;
                OnPropertyChanged(nameof(BackgroundImagePath));
            }
        }

        public string BackgroundImageName
        {
            get => _backgroundImageName;
            private set
            {
                if (_backgroundImageName == value)
                    return;

                _backgroundImageName = value;
                OnPropertyChanged(nameof(BackgroundImageName));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public ThemeSettingsWindow(MainWindow mainWindow)
        {
            InitializeComponent();

            _mainWindow = mainWindow;
            Owner = mainWindow;

            // Залишаємо ресурси поточної теми головного вікна.
            Resources.MergedDictionaries.Add(_mainWindow.Resources);

            // DataContext потрібен для властивостей фонового зображення.
            DataContext = this;

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
                new("Nebula", "Космічний пурпурний", "#C084FC", "Nebula")
            };

            ThemesList.ItemsSource = _themes;

            MarkSelectedTheme(ThemeManager.CurrentThemeKey);

            // Показуємо поточне фонове зображення, якщо воно є.
            UpdateBackgroundInfo();
        }

        private void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(
                this,
                new PropertyChangedEventArgs(propertyName));
        }

        /// <summary>
        /// Оновлює назву, шлях і видимість інформації про фон.
        /// </summary>
        private void UpdateBackgroundInfo()
        {
            string? path = ThemeManager.CurrentBackgroundImage;

            bool exists =
                !string.IsNullOrWhiteSpace(path) &&
                File.Exists(path);

            HasBackgroundImage = exists;

            BackgroundImagePath = exists
                ? path!
                : string.Empty;

            BackgroundImageName = exists
                ? Path.GetFileName(path!)
                : string.Empty;
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
                // Застосовуємо тему до головного вікна.
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
                theme.IsSelected = string.Equals(
                    theme.Key,
                    themeKey,
                    StringComparison.OrdinalIgnoreCase);
            }
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

        private void SelectImage_Click(
            object sender,
            RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "Оберіть фонове зображення",
                Filter = "Зображення (*.jpg;*.jpeg;*.png)|*.jpg;*.jpeg;*.png",
                CheckFileExists = true,
                Multiselect = false
            };

            if (dlg.ShowDialog() != true)
                return;

            try
            {
                ThemeManager.SetBackgroundImage(
                    _mainWindow,
                    dlg.FileName);

                UpdateBackgroundInfo();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    this,
                    $"Не вдалося встановити фонове зображення.\n\n{ex.Message}",
                    "Помилка",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void ClearImage_Click(
            object sender,
            RoutedEventArgs e)
        {
            try
            {
                ThemeManager.SetBackgroundImage(_mainWindow, null);

                UpdateBackgroundInfo();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    this,
                    $"Не вдалося очистити фонове зображення.\n\n{ex.Message}",
                    "Помилка",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
    }
}