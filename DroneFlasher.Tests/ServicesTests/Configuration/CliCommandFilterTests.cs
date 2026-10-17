using DroneFlasher.Services.Configuration;
using Xunit;

namespace DroneFlasher.Tests.ServicesTests.Configuration
{
    public class CliCommandFilterTests
    {
        [Fact]
        public void GetValidCommands_RemovesEmptyLinesAndComments()
        {
            // Arrange
            var rawLines = new[]
            {
                "",
                "   ",
                "# simple comment",
                "  # comment with spaces at the beginning  ",
                "set pid_p = 45"
            };

            // Act
            var result = CliCommandFilter.GetValidCommands(rawLines);

            // Assert
            Assert.Single(result);
            Assert.Equal("set pid_p = 45", result[0]);
        }

        [Fact]
        public void GetValidCommands_FiltersOutSkippedCommandsCaseInsensitively()
        {
            // Arrange
            var rawLines = new[]
            {
                "SAVE",
                "exit ",
                "Reboot",
                "bl",
                "bootloader",
                "batch start",
                "BATCH END",
                "feature airmode on"
            };

            // Act
            var result = CliCommandFilter.GetValidCommands(rawLines);

            // Assert
            Assert.Single(result);
            Assert.Equal("feature airmode on", result[0]);
        }

        [Fact]
        public void GetValidCommands_FiltersOutDefaultsAndItsVariations()
        {
            // Arrange
            var rawLines = new[]
            {
                "defaults",
                "DEFAULTS",
                "defaults nosave",
                "set name = my_drone"
            };

            // Act
            var result = CliCommandFilter.GetValidCommands(rawLines);

            // Assert
            Assert.Single(result);
            Assert.Equal("set name = my_drone", result[0]);
        }

        [Fact]
        public void GetValidCommands_ProcessesComplexDumpCorrectly()
        {
            // Arrange
            var rawLines = new[]
            {
                "# name: MyDrone",
                "   ",
                "feature -RX_PPM",
                "feature RX_SERIAL",
                "save",
                "defaults"
            };

            // Act
            var result = CliCommandFilter.GetValidCommands(rawLines);

            // Assert
            Assert.Equal(2, result.Count);
            Assert.Equal("feature -RX_PPM", result[0]);
            Assert.Equal("feature RX_SERIAL", result[1]);
        }
    }
}