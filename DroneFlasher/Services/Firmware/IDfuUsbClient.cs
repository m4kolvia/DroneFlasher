using DroneFlasher.Models;
using LibUsbDotNet.LibUsb;

namespace DroneFlasher.Services.Firmware;

public record DfuStatusInfo(byte Status, uint PollTimeoutMs, byte State, byte StringIndex);

public interface IDfuUsbClient
{
    bool IsDevicePresent();
    Task ClearStatusAsync(IUsbDevice device, IProgress<LogEntry>? log = null, CancellationToken ct = default);
    Task<DfuStatusInfo> GetStatusAsync(IUsbDevice device, CancellationToken ct = default);
    Task ExecuteMassEraseAsync(IUsbDevice device, IProgress<LogEntry>? log = null, CancellationToken ct = default);
    Task SetAddressAsync(IUsbDevice device, uint address, CancellationToken ct = default);
    Task DownloadBlockAsync(IUsbDevice device, ushort blockNumber, byte[] data, CancellationToken ct = default);
    Task LeaveDfuAsync(IUsbDevice device, IProgress<LogEntry>? log = null, CancellationToken ct = default);
}