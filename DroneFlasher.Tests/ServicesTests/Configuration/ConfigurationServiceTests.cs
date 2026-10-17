using System.Windows.Media;
using DroneFlasher.Models;
using DroneFlasher.Services.Configuration;
using Xunit;

namespace DroneFlasher.Tests.ServicesTests.Configuration
{
    public class ConfigurationServiceTests
    {

        [Fact]
        public async Task ApplyDumpAsync_FileDoesNotExist_ThrowsFileNotFoundException()
        {
            // Arrange
            var service = new ConfigurationService();
            string fakePath = "non_existent_config_file_12345.txt";
            var statusProgress = new Progress<string>();
            var logProgress = new Progress<LogEntry>();

            // Act & Assert
            await Assert.ThrowsAsync<FileNotFoundException>(() =>
                service.ApplyDumpAsync("COM1", fakePath, statusProgress, logProgress));
        }

        [Fact]
        public async Task ApplyConfigurationAsync_NonExistentPort_ThrowsOrHandlesException()
        {
            // Arrange
            var service = new ConfigurationService();
            var configLines = new[] { "feature set on" };

            var statusReports = new List<string>();
            var logReports = new List<LogEntry>();
            var statusProgress = new Progress<string>(statusReports.Add);
            var logProgress = new Progress<LogEntry>(logReports.Add);

            // Act & Assert
            await Assert.ThrowsAsync<FileNotFoundException>(() =>
                service.ApplyConfigurationAsync("COM99", configLines, statusProgress, logProgress));
        }

        private class SyncProgress<T> : IProgress<T>
        {
            private readonly Action<T> _handler;
            public SyncProgress(Action<T> handler) => _handler = handler;
            public void Report(T value) => _handler(value);
        }

        [Theory]
        [InlineData(50, 100, "50% (50/100)")]
        [InlineData(1, 2, "50% (1/2)")]
        [InlineData(3, 10, "30% (3/10)")]
        [InlineData(0, 10, "0% (0/10)")]
        public void UpdateProgress_CalculatesCorrectPercentageAndFormat(int current, int total, string expectedReport)
        {
            // Arrange
            string? actualReport = null;
            
            var statusProgress = new SyncProgress<string>(report => actualReport = report);

            // Act
            ConfigurationService.UpdateProgress(statusProgress, current, total);

            // Assert
            Assert.Equal(expectedReport, actualReport);
        }

        [Fact]
        public async Task ApplyConfigurationAsync_NoValidCommands_ReturnsEarlyWithoutOpeningPort()
        {
            // Arrange
            var service = new ConfigurationService();
            var configLines = new[] { "# Тільки коментар", "save", "defaults" };

            var logReports = new List<LogEntry>();
            var statusProgress = new Progress<string>();
            var logProgress = new Progress<LogEntry>(logReports.Add);

            // Act
            var exception = await Record.ExceptionAsync(() =>
                service.ApplyConfigurationAsync("COM99", configLines, statusProgress, logProgress));

            // Assert
            Assert.Null(exception); 
            Assert.Contains(logReports, log => log.Text == "У файлі немає команд для виконання.");
        }

        [Fact]
        public async Task ApplyDumpAsync_ValidFile_ReadsFileAndAttemptsConfiguration()
        {
            // Arrange
            var service = new ConfigurationService();
            var statusProgress = new Progress<string>();
            var logProgress = new Progress<LogEntry>();

            string tempFilePath = Path.GetTempFileName();
            await File.WriteAllLinesAsync(tempFilePath, new[] { "feature set on" });

            try
            {
                // Act & Assert
                await Assert.ThrowsAsync<FileNotFoundException>(() =>
                    service.ApplyDumpAsync("COM99", tempFilePath, statusProgress, logProgress));
            }
            finally
            {
                // Cleanup
                if (File.Exists(tempFilePath))
                    File.Delete(tempFilePath);
            }
        }
    }
}