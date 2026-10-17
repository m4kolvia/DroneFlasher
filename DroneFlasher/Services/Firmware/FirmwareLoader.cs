using System.IO;
using DroneFlasher.Helpers;

namespace DroneFlasher.Services.Firmware;

public static class FirmwareLoader
{
    public static async Task<List<HexMemoryBlock>> LoadAsync(string filePath, CancellationToken ct = default)
    {
        string extension = Path.GetExtension(filePath).ToLowerInvariant();

        if (extension == ".hex")
        {
            return await HexParser.ParseAsync(filePath, ct);
        }

        throw new InvalidOperationException($"Непідтримуваний формат прошивки: {extension}");
    }
}