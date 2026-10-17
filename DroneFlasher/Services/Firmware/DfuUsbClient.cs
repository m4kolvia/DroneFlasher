using System.IO;
using System.Windows.Media;
using DroneFlasher.Constants;
using DroneFlasher.Models;
using LibUsbDotNet.LibUsb;
using LibUsbDotNet.Main;

namespace DroneFlasher.Services.Firmware;

public class DfuUsbClient : IDfuUsbClient
{
    public bool IsDevicePresent()
    {
        try
        {
            using var context = new UsbContext();
            using var deviceList = context.List();

            return deviceList.Any(d => d.VendorId == DfuConstants.StmDfuVid && d.ProductId == DfuConstants.StmDfuPid);
        }
        catch
        {
            return false;
        }
    }

    public async Task ClearStatusAsync(IUsbDevice device, IProgress<LogEntry>? log = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var setup = new UsbSetupPacket(
            (byte)(UsbCtrlFlags.RequestType_Class | UsbCtrlFlags.Recipient_Interface | UsbCtrlFlags.Direction_Out),
            DfuConstants.DFU_CLRSTATUS, 0, 0, 0);

        try
        {
            int written;
            if (device is UsbDevice concreteDevice)
            {
                written = await concreteDevice.ControlTransferAsync(setup, null, 0, 0);
            }
            else
            {
                written = await Task.Run(() => device.ControlTransfer(setup, Array.Empty<byte>(), 0, 0), ct);
            }

            ct.ThrowIfCancellationRequested();
            log?.Report(new LogEntry($"[DFU] CLRSTATUS виконано. Result={written}", LogLevel.Gray));
            await Task.Delay(200, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            log?.Report(new LogEntry($"[DFU] ⚠ CLRSTATUS: {ex.Message}", LogLevel.Warning));
        }
    }

    public async Task<DfuStatusInfo> GetStatusAsync(IUsbDevice device, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        const int statusLength = 6;
        byte[] buffer = new byte[statusLength];

        var setup = new UsbSetupPacket(
            (byte)(UsbCtrlFlags.RequestType_Class | UsbCtrlFlags.Recipient_Interface | UsbCtrlFlags.Direction_In),
            DfuConstants.DFU_GETSTATUS, 0, 0, statusLength);

        try
        {
            int read;
            if (device is UsbDevice concreteDevice)
            {
                read = await concreteDevice.ControlTransferAsync(setup, buffer, 0, statusLength);
            }
            else
            {
                read = await Task.Run(() => device.ControlTransfer(setup, buffer, 0, statusLength), ct);
            }

            ct.ThrowIfCancellationRequested();

            if (read != statusLength)
                throw new IOException($"DFU GETSTATUS повернув {read} байт замість {statusLength}.");

            uint pollTimeout = (uint)(buffer[1] | (buffer[2] << 8) | (buffer[3] << 16));
            return new DfuStatusInfo(buffer[0], pollTimeout, buffer[4], buffer[5]);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new IOException($"DFU GETSTATUS не виконано: {ex.Message}", ex);
        }
    }

    public async Task ExecuteMassEraseAsync(IUsbDevice device, IProgress<LogEntry>? log = null, CancellationToken ct = default)
    {
        byte[] command = [0x41];
        log?.Report(new LogEntry("[DFU] Надсилання команди Mass Erase (0x41)...", LogLevel.Warning));

        await SendControlAsync(device, DfuConstants.DFU_DNLOAD, 0, command, ct);
        await WaitForOperationCompleteAsync(device, "Mass Erase", log, ct);

        DfuStatusInfo status = await GetStatusAsync(device, ct);
        log?.Report(new LogEntry($"[DFU] Стан після Mass Erase: State={status.State}, Status={status.Status}", LogLevel.Gray));

        if (status.Status != 0)
            throw new InvalidOperationException($"[DFU] Помилка після Mass Erase: Status={status.Status}, State={status.State}");

        if (status.State != DfuConstants.DFU_STATE_DNLOAD_IDLE && status.State != DfuConstants.DFU_STATE_DFU_IDLE)
            throw new InvalidOperationException($"[DFU] Неочікуваний стан після Mass Erase: State={status.State}");

        log?.Report(new LogEntry("[DFU] ✓ Mass Erase завершено, стан DFU перевірено.", LogLevel.Success));
    }

    public async Task SetAddressAsync(IUsbDevice device, uint address, CancellationToken ct = default)
    {
        byte[] command =
        [
            0x21,
            (byte)(address & 0xFF),
            (byte)((address >> 8) & 0xFF),
            (byte)((address >> 16) & 0xFF),
            (byte)((address >> 24) & 0xFF)
        ];

        await SendControlAsync(device, DfuConstants.DFU_DNLOAD, 0, command, ct);
        await WaitForOperationCompleteAsync(device, $"Set Address 0x{address:X8}", null, ct);
    }

    public async Task DownloadBlockAsync(IUsbDevice device, ushort blockNumber, byte[] data, CancellationToken ct = default)
    {
        await SendControlAsync(device, DfuConstants.DFU_DNLOAD, blockNumber, data, ct);
        await WaitForOperationCompleteAsync(device, $"Запис блоку #{blockNumber}", null, ct);
    }

    public async Task LeaveDfuAsync(IUsbDevice device, IProgress<LogEntry>? log = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        await SendControlAsync(device, DfuConstants.DFU_DNLOAD, 0, Array.Empty<byte>(), ct);
        log?.Report(new LogEntry("[DFU] Команду виходу з DFU надіслано.", LogLevel.Info));

        try
        {
            DfuStatusInfo status = await GetStatusAsync(device, ct);
            if (status.Status != 0)
                throw new InvalidOperationException($"[DFU] Помилка під час виходу: Status={status.Status}, State={status.State}");

            log?.Report(new LogEntry($"[DFU] Відповідь на вихід отримано: State={status.State}, Status={status.Status}.", LogLevel.Success));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            if (IsDevicePresent())
                throw new IOException("[DFU] Не вдалося підтвердити вихід із DFU, а пристрій усе ще присутній.", ex);

            log?.Report(new LogEntry("[DFU] Пристрій зник з USB після команди виходу. Перезапуск слід підтвердити повторним виявленням.", LogLevel.Warning));
        }
    }

    private async Task SendControlAsync(IUsbDevice device, byte request, ushort value, byte[] data, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var setup = new UsbSetupPacket(
            (byte)(UsbCtrlFlags.RequestType_Class | UsbCtrlFlags.Recipient_Interface | UsbCtrlFlags.Direction_Out),
            request, value, 0, (ushort)data.Length);

        int written;
        if (device is UsbDevice concreteDevice)
        {
            written = await concreteDevice.ControlTransferAsync(setup, data, 0, data.Length);
        }
        else
        {
            written = await Task.Run(() => device.ControlTransfer(setup, data, 0, data.Length), ct);
        }

        ct.ThrowIfCancellationRequested();

        if (written != data.Length)
            throw new IOException($"ControlTransfer: передано {written} з {data.Length} байт.");
    }

    private async Task WaitForOperationCompleteAsync(IUsbDevice device, string operation, IProgress<LogEntry>? log, CancellationToken ct)
    {
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            DfuStatusInfo status = await GetStatusAsync(device, ct);

            if (status.Status != 0)
            {
                log?.Report(new LogEntry($"[DFU] ✖ {operation}: Status={status.Status}, State={status.State}", Brushes.IndianRed));
                throw new InvalidOperationException($"DFU помилка під час '{operation}'. Status={status.Status}, State={status.State}");
            }

            switch (status.State)
            {
                case DfuConstants.DFU_STATE_DNLOAD_SYNC:
                case DfuConstants.DFU_STATE_DNLOAD_BUSY:
                    int delay = Math.Max((int)status.PollTimeoutMs, 10);
                    await Task.Delay(delay, ct);
                    continue;

                case DfuConstants.DFU_STATE_DNLOAD_IDLE:
                case DfuConstants.DFU_STATE_DFU_IDLE:
                    return;

                case DfuConstants.DFU_STATE_ERROR:
                    throw new InvalidOperationException($"DFU перейшов у ERROR під час '{operation}'.");

                default:
                    throw new InvalidOperationException($"Неочікуваний DFU state {status.State} під час '{operation}'.");
            }
        }
    }
}