using DroneFlasher.Models;
using DroneFlasher.Services.Configuration;
using DroneFlasher.Services.Firmware;
using DroneFlasher.Services.Usb;
using DroneFlasher.ViewModels;
using System.IO;
using System.Windows.Media;
using Xunit;

namespace DroneFlasher.Tests.ViewModelsTests
{
    public class MainViewModelTests
    {
        // Допоміжний метод для створення ViewModel із фейковими (або null) сервісами
        private MainViewModel CreateViewModel()
        {
            // Оскільки ми тестуємо логіку ViewModel, а не самі сервіси USB/COM, 
            // для таких тестів можна передати null або мінімальні реалізації.
            return new MainViewModel(
                configService: null!,
                dfuService: null!,
                dfuWatcherService: null!,
                fcWatcherService: null!
            );
        }

        [Fact]
        public void InitialState_DefaultValuesAreCorrect()
        {
            // Arrange & Act
            var vm = CreateViewModel();

            // Assert
            Assert.False(vm.IsBusy);
            Assert.False(vm.IsAutoFlashEnabled);
            Assert.Equal(string.Empty, vm.FirmwarePath);
            Assert.Equal(string.Empty, vm.DumpPath);
            Assert.Equal("Очікування налаштувань...", vm.StatusText);
            Assert.Empty(vm.LogEntries);
        }

        [Fact]
        public void FirmwarePath_WhenFileDoesNotExist_SetsErrorMessageStatus()
        {
            // Arrange
            var vm = CreateViewModel();
            vm.IsAutoFlashEnabled = true; // Увімкнемо автозапуск, щоб спрацювала валідація
            string nonExistentPath = Path.Combine(
    Path.GetTempPath(),
    $"{Guid.NewGuid()}.hex");

            // Act
            vm.FirmwarePath = nonExistentPath;

            // Assert
            Assert.Equal(nonExistentPath, vm.FirmwarePath);
            Assert.Equal("Помилка: файл не знайдено!", vm.StatusText);
            Assert.Equal(Brushes.IndianRed, vm.StatusBrush);
        }

        [Fact]
        public void FirmwarePath_WhenFileExists_SetsWaitingStatus()
        {
            // Arrange
            var vm = CreateViewModel();
            vm.IsAutoFlashEnabled = true; // Увімкнемо автозапуск для валідації
            string tempFilePath = Path.GetTempFileName();

            try
            {
                // Act
                vm.FirmwarePath = tempFilePath;

                // Assert
                Assert.Equal(tempFilePath, vm.FirmwarePath);
                Assert.Equal("Автозапуск: очікування дрона...", vm.StatusText);
                Assert.Equal(Brushes.DodgerBlue, vm.StatusBrush);
            }
            finally
            {
                // Cleanup
                if (File.Exists(tempFilePath)) File.Delete(tempFilePath);
            }
        }

        [Fact]
        public void ClearFirmwareCommand_ResetsFirmwarePath()
        {
            // Arrange
            var vm = CreateViewModel();
            vm.FirmwarePath = @"C:\some_path.hex";

            // Перевіряємо, що команда активна
            Assert.True(vm.ClearFirmwareCommand.CanExecute(null));

            // Act
            vm.ClearFirmwareCommand.Execute(null);

            // Assert
            Assert.Equal(string.Empty, vm.FirmwarePath);
        }

        [Fact]
        public void ToggleAutoFlashCommand_ChangesState()
        {
            // Arrange
            var vm = CreateViewModel();
            Assert.False(vm.IsAutoFlashEnabled);

            // Act
            vm.ToggleAutoFlashCommand.Execute(null);

            // Assert
            Assert.True(vm.IsAutoFlashEnabled);

            // Act (вимкнення)
            vm.ToggleAutoFlashCommand.Execute(null);

            // Assert
            Assert.False(vm.IsAutoFlashEnabled);
        }

        [Fact]
        public void ClearLogCommand_EmptiesLogEntries()
        {
            var vm = CreateViewModel();

            vm.LogEntries.Add(
                new LogEntry("Test message", Brushes.Gray));

            vm.ClearLogCommand.Execute(null);

            Assert.Empty(vm.LogEntries);
        }
    }
}