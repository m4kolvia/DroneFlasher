using DroneFlasher.Services.Configuration;

namespace DroneFlasher.Tests.ServicesTests.Configuration
{
    public class CliResponseParserTests
    {
        [Theory]
        [InlineData("ERROR: Invalid argument", true)]
        [InlineData("### CLI Error ###", true)]
        [InlineData("unknown command: foo", true)]
        [InlineData("invalid param", true)]
        [InlineData("save failed", true)]
        [InlineData("parse error at line 4", true)]
        [InlineData("Command successful #", false)]
        [InlineData("MSP version received", false)]
        [InlineData("   ", false)]
        [InlineData(null, false)]
        public void ContainsError_ChecksVariousResponses_ReturnsExpectedResult(string? response, bool expectedResult)
        {
            // Act
            bool result = CliResponseParser.ContainsError(response!);

            // Assert
            Assert.Equal(expectedResult, result);
        }

        [Fact]
        public void ExtractCleanError_RemovesOriginalCommandAndPrompt_ReturnsOnlyErrorText()
        {
            // Arrange
            string originalCommand = "set invalid_param = 123";
            string response = "set invalid_param = 123\r\n### Unrecognized command ###\r\n#";

            // Act
            string cleanError = CliResponseParser.ExtractCleanError(response, originalCommand);

            // Assert
            Assert.Equal("### Unrecognized command ###", cleanError);
        }

        [Fact]
        public void ExtractCleanError_MultipleErrorLines_JoinsWithPipe()
        {
            // Arrange
            string originalCommand = "feature fake_feature";
            string response = "feature fake_feature\r\nInvalid feature\r\nparse error\r\n#";

            // Act
            string cleanError = CliResponseParser.ExtractCleanError(response, originalCommand);

            // Assert
            Assert.Equal("Invalid feature | parse error", cleanError);
        }

        [Fact]
        public void ExtractCleanError_EmptyOrWhitespaceResponse_ReturnsFallbackMessage()
        {
            // Arrange
            string originalCommand = "some command";
            string emptyResponse = "\r\n  \r\n#\r\n";

            // Act
            string cleanError = CliResponseParser.ExtractCleanError(emptyResponse, originalCommand);

            // Assert
            Assert.Equal("ПОМИЛКА CLI", cleanError);
        }
    }
}