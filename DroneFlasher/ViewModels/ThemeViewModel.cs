using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows.Input;
using DroneFlasher.Helpers;
using DroneFlasher.Models;

namespace DroneFlasher.ViewModels
{
    public class ThemeViewModel : INotifyPropertyChanged
    {
        private readonly Action<string> _applyThemeAction;
        private readonly Action<string?, double?> _applyBackgroundAction; // Змінили сигнатуру на прийняття opacity
        private readonly Func<string?> _openFileDialogFunc;
        private readonly Action<string, string> _showErrorAction;

        private bool _hasBackgroundImage;
        private string _backgroundImagePath = string.Empty;
        private string _backgroundImageName = string.Empty;
        private double _backgroundOpacity = 0.2;

        public ObservableCollection<ThemeOption> Themes { get; }

        public bool HasBackgroundImage
        {
            get => _hasBackgroundImage;
            private set
            {
                if (_hasBackgroundImage == value) return;
                _hasBackgroundImage = value;
                OnPropertyChanged(nameof(HasBackgroundImage));
            }
        }

        public string BackgroundImagePath
        {
            get => _backgroundImagePath;
            private set
            {
                if (_backgroundImagePath == value) return;
                _backgroundImagePath = value;
                OnPropertyChanged(nameof(BackgroundImagePath));
            }
        }

        public string BackgroundImageName
        {
            get => _backgroundImageName;
            private set
            {
                if (_backgroundImageName == value) return;
                _backgroundImageName = value;
                OnPropertyChanged(nameof(BackgroundImageName));
            }
        }

        // Властивість для повзунка (від 5% до 100%)
        public double BackgroundOpacity
        {
            get => _backgroundOpacity;
            set
            {
                if (_backgroundOpacity == value) return;
                _backgroundOpacity = value;
                OnPropertyChanged(nameof(BackgroundOpacity));

                // Оновлюємо прозорість "на льоту", якщо фон встановлено
                if (HasBackgroundImage)
                {
                    _applyBackgroundAction(BackgroundImagePath, _backgroundOpacity);
                }
            }
        }

        public ICommand SelectThemeCommand { get; }
        public ICommand SelectImageCommand { get; }
        public ICommand ClearImageCommand { get; }

        public event PropertyChangedEventHandler? PropertyChanged;

        public ThemeViewModel(
            Action<string> applyThemeAction,
            Action<string?, double?> applyBackgroundAction,
            Func<string?> openFileDialogFunc,
            Action<string, string> showErrorAction)
        {
            _applyThemeAction = applyThemeAction;
            _applyBackgroundAction = applyBackgroundAction;
            _openFileDialogFunc = openFileDialogFunc;
            _showErrorAction = showErrorAction;

            // Завантажуємо поточне значення з ThemeManager
            _backgroundOpacity = ThemeManager.CurrentBackgroundOpacity;

            Themes = new ObservableCollection<ThemeOption>
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

            SelectThemeCommand = new RelayCommand<string>(ExecuteSelectTheme);
            SelectImageCommand = new RelayCommand<object>(_ => ExecuteSelectImage());
            ClearImageCommand = new RelayCommand<object>(_ => ExecuteClearImage());

            MarkSelectedTheme(ThemeManager.CurrentThemeKey);
            UpdateBackgroundInfo();
        }

        private void ExecuteSelectTheme(string themeKey)
        {
            if (string.IsNullOrEmpty(themeKey)) return;

            try
            {
                _applyThemeAction(themeKey);
                MarkSelectedTheme(themeKey);
            }
            catch (Exception ex)
            {
                _showErrorAction("Помилка зміни теми", $"Не вдалося застосувати тему.\n\n{ex.Message}");
            }
        }

        private void ExecuteSelectImage()
        {
            string? filePath = _openFileDialogFunc();
            if (filePath != null)
            {
                try
                {
                    _applyBackgroundAction(filePath, _backgroundOpacity);
                    UpdateBackgroundInfo();
                }
                catch (Exception ex)
                {
                    _showErrorAction("Помилка", $"Не вдалося встановити фонове зображення.\n\n{ex.Message}");
                }
            }
        }

        private void ExecuteClearImage()
        {
            try
            {
                _applyBackgroundAction(null, null);
                UpdateBackgroundInfo();
            }
            catch (Exception ex)
            {
                _showErrorAction("Помилка", $"Не вдалося очистити фонове зображення.\n\n{ex.Message}");
            }
        }

        private void MarkSelectedTheme(string themeKey)
        {
            foreach (var theme in Themes)
            {
                theme.IsSelected = string.Equals(theme.Key, themeKey, StringComparison.OrdinalIgnoreCase);
            }
        }

        private void UpdateBackgroundInfo()
        {
            string? path = ThemeManager.CurrentBackgroundImage;
            bool exists = !string.IsNullOrWhiteSpace(path) && File.Exists(path);

            HasBackgroundImage = exists;
            BackgroundImagePath = exists ? path! : string.Empty;
            BackgroundImageName = exists ? Path.GetFileName(path!) : string.Empty;
        }

        private void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class RelayCommand<T> : ICommand
    {
        private readonly Action<T> _execute;
        public RelayCommand(Action<T> execute) => _execute = execute;
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => _execute((T)parameter!);
        public event EventHandler? CanExecuteChanged { add { } remove { } }
    }
}