using DroneFlasher.Models;
using System.Globalization;
using System.IO;

namespace DroneFlasher.Helpers;

public class HexMemoryBlock
{
    public uint Address { get; set; }
    public byte[] Data { get; set; } = [];
}

public static class HexParser
{
    public static async Task<List<HexMemoryBlock>> ParseAsync(string hexFilePath, CancellationToken ct = default)
    {
        var rawMemory = new SortedDictionary<uint, byte>();
        uint upperAddress = 0;

        using var reader = new StreamReader(hexFilePath);
        string? line;

        while ((line = await reader.ReadLineAsync(ct)) != null)
        {
            ct.ThrowIfCancellationRequested();
            line = line.Trim();
            if (string.IsNullOrEmpty(line) || !line.StartsWith(':'))
                continue;

            int byteCount = int.Parse(line.Substring(1, 2), NumberStyles.HexNumber);
            uint address = uint.Parse(line.Substring(3, 4), NumberStyles.HexNumber);
            int recordType = int.Parse(line.Substring(7, 2), NumberStyles.HexNumber);

            if (recordType == DfuConstants.DFU_DETACH) // Data Record
            {
                uint fullAddress = upperAddress | address;
                for (int i = 0; i < byteCount; i++)
                {
                    byte b = byte.Parse(line.Substring(9 + (i * 2), 2), NumberStyles.HexNumber);
                    rawMemory[fullAddress + (uint)i] = b;
                }
            }
            else if (recordType == DfuConstants.DFU_DNLOAD) // EOF
            {
                break;
            }
            else if (recordType == DfuConstants.DFU_CLRSTATUS) // Extended Linear Address Record
            {
                upperAddress = uint.Parse(line.Substring(9, 4), NumberStyles.HexNumber) << 16;
            }
        }

        var blocks = new List<HexMemoryBlock>();
        uint? currentStart = null;
        var currentData = new List<byte>();

        foreach (var kvp in rawMemory)
        {
            if (!currentStart.HasValue)
            {
                currentStart = kvp.Key;
                currentData.Add(kvp.Value);
            }
            else if (kvp.Key == currentStart.Value + currentData.Count)
            {
                currentData.Add(kvp.Value);
            }
            else
            {
                blocks.Add(new HexMemoryBlock
                {
                    Address = currentStart.Value,
                    Data = currentData.ToArray()
                });
                currentStart = kvp.Key;
                currentData.Clear();
                currentData.Add(kvp.Value);
            }
        }

        if (currentStart.HasValue && currentData.Count > 0)
        {
            blocks.Add(new HexMemoryBlock
            {
                Address = currentStart.Value,
                Data = currentData.ToArray()
            });
        }

        return blocks;
    }
}