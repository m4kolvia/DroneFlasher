using DroneFlasher.Helpers;
using DroneFlasher.ViewModels;
using Microsoft.Win32;
using System.Windows;
using System.Windows.Input;

namespace DroneFlasher.Views
{
    public partial class ThemeSettingsWindow : Window
    {
        public ThemeSettingsWindow(MainWindow mainWindow)
        {
            InitializeComponent();

            Owner = mainWindow;
            Resources.MergedDictionaries.Add(mainWindow.Resources);

            DataContext = new ThemeViewModel(
                applyThemeAction: themeKey =>
                    ThemeManager.ApplyTheme(mainWindow, themeKey),

                applyBackgroundAction: (imagePath, opacity) =>
                    ThemeManager.SetBackgroundImage(mainWindow, imagePath, opacity),

                openFileDialogFunc: () =>
                {
                    var dlg = new OpenFileDialog
                    {
                        Title = "Оберіть фонове зображення",
                        Filter = "Зображення (*.jpg;*.jpeg;*.png)|*.jpg;*.jpeg;*.png",
                        CheckFileExists = true,
                        Multiselect = false
                    };

                    return dlg.ShowDialog() == true ? dlg.FileName : null;
                },

                showErrorAction: (title, message) =>
                    MessageBox.Show(
                        this,
                        message,
                        title,
                        MessageBoxButton.OK,
                        MessageBoxImage.Error)
            );
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
                DragMove();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}