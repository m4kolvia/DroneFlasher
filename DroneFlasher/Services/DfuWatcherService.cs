using System.IO.Ports;
using System.Management;
using LibUsbDotNet.LibUsb;

namespace DroneFlasher.Services;

public class DfuWatcherService : IDisposable
{
    private const ushort StmDfuVid = 0x0483;
    private const ushort StmDfuPid = 0xDF11;

    private readonly UsbContext _usbContext = new();

    public async Task<bool> IsDeviceInDfuModeAsync()
    {
        return await Task.Run(() =>
        {
            try
            {
                using var deviceList = _usbContext.List();

                return deviceList.Any(d =>
                    d.VendorId == StmDfuVid &&
                    d.ProductId == StmDfuPid);
            }
            catch
            {
                return false;
            }
        });
    }

    public List<string> GetConnectedDevicesLog()
    {
        var log = new List<string>();
        try
        {
            string[] comPorts = SerialPort.GetPortNames();
            log.Add(comPorts.Length == 0 ? "Порти: не знайдено" : $"Порти: {string.Join(", ", comPorts.OrderBy(p => p))}");

            using var searcher = new ManagementObjectSearcher("SELECT Name, DeviceID FROM Win32_PnPEntity WHERE Present = TRUE");

            bool found = false;
            foreach (ManagementObject device in searcher.Get())
            {
                string name = device["Name"]?.ToString() ?? string.Empty;
                string deviceId = device["DeviceID"]?.ToString() ?? string.Empty;

                bool isStm32 = name.Contains("STM32", StringComparison.OrdinalIgnoreCase)
                            || name.Contains("BOOTLOADER", StringComparison.OrdinalIgnoreCase)
                            || deviceId.Contains("VID_0483", StringComparison.OrdinalIgnoreCase);

                if (isStm32)
                {
                    log.Add($"  {name} | ID: {deviceId}");
                    found = true;
                }
            }

            if (!found)
                log.Add("  STM32 / BOOTLOADER не знайдено.");
        }
        catch (Exception ex)
        {
            log.Add($"[DFU] Помилка WMI: {ex.Message}");
        }

        return log;
    }

    public void Dispose()
    {
        _usbContext.Dispose();
        GC.SuppressFinalize(this);
    }
}