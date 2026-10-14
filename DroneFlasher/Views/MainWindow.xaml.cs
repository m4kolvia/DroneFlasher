using DroneFlasher.Helpers;
using DroneFlasher.ViewModels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace DroneFlasher.Views
{
    public partial class MainWindow : Window
    {
        public MainWindow(MainViewModel viewModel)
        {
            InitializeComponent();

            DataContext = viewModel;

            ThemeManager.ApplyTheme(this, ThemeManager.CurrentThemeKey);
        }

        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                DragMove();
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

        private void MaximizeButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }

        private void AutoFlashCard_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (DataContext is MainViewModel vm)
            {
                vm.IsAutoFlashEnabled = true;
                e.Handled = true;
            }
        }
        private void ManualFlashCard_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (DataContext is MainViewModel vm)
            {
                vm.IsAutoFlashEnabled = false;
                e.Handled = true;
            }
        }

        private void SettingsButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            var settingsWindow = new ThemeSettingsWindow(this);
            settingsWindow.ShowDialog();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
    }
}