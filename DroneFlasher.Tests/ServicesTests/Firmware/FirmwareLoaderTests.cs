using DroneFlasher.Services.Firmware;

namespace DroneFlasher.Tests.ServicesTests.Firmware
{
    public class FirmwareLoaderTests
    {
        [Theory]
        [InlineData("firmware.bin")]
        [InlineData("image.jpg")]
        [InlineData("unsupported_file.txt")]
        [InlineData("no_extension_file")]
        public async Task LoadAsync_UnsupportedExtension_ThrowsInvalidOperationException(string fileName)
        {
            // Act & Assert
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                FirmwareLoader.LoadAsync(fileName));

            Assert.Contains("Непідтримуваний формат прошивки", exception.Message);
        }

        [Fact]
        public async Task LoadAsync_ValidHexFile_PassesToHexParserSuccessfully()
        {
            // Arrange
            string tempFilePath = Path.GetTempFileName();
            string hexFilePath = Path.ChangeExtension(tempFilePath, ".hex");
            File.Move(tempFilePath, hexFilePath);

            try
            {
                await File.WriteAllTextAsync(hexFilePath, ":00000001FF\n");

                // Act
                var result = await FirmwareLoader.LoadAsync(hexFilePath);

                // Assert
                Assert.NotNull(result);
            }
            finally
            {
                // Cleanup
                if (File.Exists(hexFilePath))
                {
                    File.Delete(hexFilePath);
                }
            }
        }
    }
}