using DroneFlasher.Helpers;
using Xunit;

namespace DroneFlasher.Tests.Helpers;

public class HexParserTests
{
    private static string CreateHexFile(params string[] lines)
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"{Guid.NewGuid()}.hex");

        File.WriteAllLines(path, lines);
        return path;
    }

    private static async Task<List<HexMemoryBlock>> ParseHexAsync(
        params string[] lines)
    {
        string path = CreateHexFile(lines);

        try
        {
            return await HexParser.ParseAsync(path);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ParseAsync_EmptyFile_ReturnsEmptyList()
    {
        // Arrange & Act
        var result = await ParseHexAsync();

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public async Task ParseAsync_OneDataRecord_ReturnsCorrectBlock()
    {
        // Arrange
        var result = await ParseHexAsync(
            ":03000000010203F7",
            ":00000001FF");

        // Assert
        var block = Assert.Single(result);

        Assert.Equal(0u, block.Address);
        Assert.Equal(new byte[] { 0x01, 0x02, 0x03 }, block.Data);
    }

    [Fact]
    public async Task ParseAsync_AdjacentAddresses_MergesIntoOneBlock()
    {
        // Arrange
        var result = await ParseHexAsync(
            ":020000000102FB",
            ":020002000304F5",
            ":00000001FF");

        // Assert
        var block = Assert.Single(result);

        Assert.Equal(0u, block.Address);
        Assert.Equal(
            new byte[] { 0x01, 0x02, 0x03, 0x04 },
            block.Data);
    }

    [Fact]
    public async Task ParseAsync_NonAdjacentAddresses_ReturnsSeparateBlocks()
    {
        // Arrange
        var result = await ParseHexAsync(
            ":020000000102FB",
            ":020010000304E7",
            ":00000001FF");

        // Assert
        Assert.Equal(2, result.Count);

        Assert.Equal(0u, result[0].Address);
        Assert.Equal(new byte[] { 0x01, 0x02 }, result[0].Data);

        Assert.Equal(0x10u, result[1].Address);
        Assert.Equal(new byte[] { 0x03, 0x04 }, result[1].Data);
    }

    [Fact]
    public async Task ParseAsync_ExtendedLinearAddress_UsesUpperAddress()
    {
        // Arrange
        var result = await ParseHexAsync(
            ":020000040800F2",
            ":02001000AABB89",
            ":00000001FF");

        // Assert
        var block = Assert.Single(result);

        Assert.Equal(0x08000010u, block.Address);
        Assert.Equal(new byte[] { 0xAA, 0xBB }, block.Data);
    }

    [Fact]
    public async Task ParseAsync_EofRecord_IgnoresFollowingRecords()
    {
        // Arrange
        var result = await ParseHexAsync(
            ":0100000001FE",
            ":00000001FF",
            ":0100100002ED");

        // Assert
        var block = Assert.Single(result);

        Assert.Equal(0u, block.Address);
        Assert.Equal(new byte[] { 0x01 }, block.Data);
    }

    [Fact]
    public async Task ParseAsync_WhitespaceAndEmptyLines_IgnoresThem()
    {
        // Arrange
        var result = await ParseHexAsync(
            "",
            "   ",
            "  :0100000001FE  ",
            ":00000001FF");

        // Assert
        var block = Assert.Single(result);

        Assert.Equal(0u, block.Address);
        Assert.Equal(new byte[] { 0x01 }, block.Data);
    }

    [Fact]
    public async Task ParseAsync_CancellationRequested_ThrowsOperationCanceledException()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        string path = CreateHexFile(":0100000001FE");

        try
        {
            // Act & Assert
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => HexParser.ParseAsync(path, cts.Token));
        }
        finally
        {
            File.Delete(path);
        }
    }
}