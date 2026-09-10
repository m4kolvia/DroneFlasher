using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Management;
using System.Threading;
using System.Threading.Tasks;
using LibUsbDotNet.LibUsb;
using LibUsbDotNet.Main;

namespace DroneFlasher.Services;

public class DfuService
{
    private const ushort StmDfuVid = 0x0483;
    private const ushort StmDfuPid = 0xDF11;

    private const byte DFU_GETSTATUS = 0x03;
    private const byte DFU_CLRSTATUS = 0x04;
    private const byte STATE_DFU_ERROR = 10;

    public record DfuStatusInfo(byte Status, uint PollTimeoutMs, byte State, byte StringIndex);

    public async Task RebootToBootloaderAsync(string portName)
    {
        using var port = new SerialPort(portName, 115200)
        {
            ReadTimeout = 1000,
            WriteTimeout = 1000,
            DtrEnable = true,
            RtsEnable = true,
            NewLine = "\r\n"
        };

        port.Open();
        await Task.Delay(300);

        port.DiscardInBuffer();
        port.DiscardOutBuffer();

        // Вхід у CLI
        port.Write("#");
        await Task.Delay(300);

        // Команда переходу в бутлоадер
        port.Write("bl\r\n");

        // Утримуємо порт відкритим, щоб буфери Windows встигли вийти в контролер
        await Task.Delay(1500);
    }

    public async Task<bool> IsDeviceInDfuModeAsync()
    {
        return await Task.Run(() =>
        {
            try
            {
                using var context = new UsbContext();
                using var deviceList = context.List();

                return deviceList.Any(d => d.VendorId == StmDfuVid && d.ProductId == StmDfuPid);
            }
            catch
            {
                return false;
            }
        });
    }

    public async Task<DfuStatusInfo> QueryDfuStatusAsync(IProgress<string>? log = null, CancellationToken ct = default)
    {
        return await Task.Run(async () =>
        {
            using var context = new UsbContext();
            using var deviceList = context.List();

            var device = deviceList.FirstOrDefault(d => d.VendorId == StmDfuVid && d.ProductId == StmDfuPid)
                ?? throw new InvalidOperationException("Пристрій 0483:DF11 не знайдено в USB стеку.");

            if (!device.IsOpen)
                device.Open();

            try
            {
                device.ClaimInterface(0);
                log?.Report("[DFU] USB Interface 0 захоплено.");
                await Task.Delay(150, ct);

                // 1. Примусове скидання завислого стану без очікування
                var clrSetup = new UsbSetupPacket(
                    (byte)(UsbCtrlFlags.RequestType_Class | UsbCtrlFlags.Recipient_Interface | UsbCtrlFlags.Direction_Out),
                    DFU_CLRSTATUS, 0, 0, 0);

                try
                {
                    device.ControlTransfer(clrSetup, Array.Empty<byte>(), 0, 0);
                }
                catch { }

                await Task.Delay(100, ct);

                // 2. Зчитування актуального статусу (має стати State=2 dfuIDLE)
                byte[] statusBytes = new byte[6];
                var getStatusPacket = new UsbSetupPacket(
                    (byte)(UsbCtrlFlags.RequestType_Class | UsbCtrlFlags.Recipient_Interface | UsbCtrlFlags.Direction_In),
                    DFU_GETSTATUS, 0, 0, 6);

                int read = device.ControlTransfer(getStatusPacket, statusBytes, 0, 6);
                if (read != 6)
                    throw new InvalidOperationException($"Неповна відповідь DFU GETSTATUS ({read} байт).");

                byte status = statusBytes[0];
                uint pollTimeout = (uint)(statusBytes[1] | (statusBytes[2] << 8) | (statusBytes[3] << 16));
                byte state = statusBytes[4];
                byte strIdx = statusBytes[5];

                log?.Report($"[DFU] Актуальний стан чіпа: State={state}, Status={status}, PollTimeout={pollTimeout}ms");

                return new DfuStatusInfo(status, pollTimeout, state, strIdx);
            }
            finally
            {
                try { device.ReleaseInterface(0); } catch { }
                try { if (device.IsOpen) device.Close(); } catch { }
            }
        }, ct);
    }

    public List<string> GetConnectedDevicesLog()
    {
        var log = new List<string>();
        try
        {
            string[] comPorts = SerialPort.GetPortNames();
            log.Add(comPorts.Length == 0 ? "Порти: не знайдено" : $"Порти: {string.Join(", ", comPorts.OrderBy(p => p))}");

            using var searcher = new ManagementObjectSearcher(
                "SELECT Name, DeviceID FROM Win32_PnPEntity WHERE Present = TRUE");

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
}