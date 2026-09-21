using System.Diagnostics;
using System.IO;
using System.IO.Ports;
using System.Windows.Media;
using DroneFlasher.Helpers;
using DroneFlasher.Models;
using LibUsbDotNet.LibUsb;
using LibUsbDotNet.Main;

namespace DroneFlasher.Services;

public class DfuService : IDisposable
{
    private const bool TestOnly = true;

    private const ushort StmDfuVid = 0x0483;
    private const ushort StmDfuPid = 0xDF11;

    private const byte DFU_DNLOAD = 0x01;
    private const byte DFU_GETSTATUS = 0x03;
    private const byte DFU_CLRSTATUS = 0x04;

    private const byte DFU_STATE_APP_IDLE = 0;
    private const byte DFU_STATE_DFU_IDLE = 2;
    private const byte DFU_STATE_DNLOAD_SYNC = 3;
    private const byte DFU_STATE_DNLOAD_BUSY = 4;
    private const byte DFU_STATE_DNLOAD_IDLE = 5;
    private const byte DFU_STATE_MANIFEST = 6;
    private const byte DFU_STATE_ERROR = 10;

    private const int TransferBlockSize = 2048;
    private const uint DefaultBaseAddress = 0x08000000;

    private readonly UsbContext _usbContext = new();

    public record DfuStatusInfo(byte Status, uint PollTimeoutMs, byte State, byte StringIndex);

    public async Task RebootToBootloaderAsync(string portName)
    {
        try
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
            await Task.Delay(150);

            port.DiscardInBuffer();
            port.DiscardOutBuffer();

            port.Write("#");
            await Task.Delay(150);

            port.Write("bl\r\n");
            await Task.Delay(500);
        }
        catch { }
    }

    public async Task<bool> EnterBootloaderAndWaitAsync(string? portName, IProgress<string>? statusProgress = null, IProgress<LogEntry>? logProgress = null, CancellationToken ct = default)
    {
        statusProgress?.Report($"Налаштування пристрою...");

        if (!string.IsNullOrWhiteSpace(portName))
        {
            logProgress?.Report(new LogEntry($"[DFU] Переведення {portName} у Bootloader...", Brushes.Orange));

            await RebootToBootloaderAsync(portName);
            await Task.Delay(700, ct);
        }

        logProgress?.Report(new LogEntry("[DFU] Очікування 0483:DF11...", Brushes.LightBlue));

        var stopwatch = Stopwatch.StartNew();

        while (stopwatch.ElapsedMilliseconds < 5000)
        {
            ct.ThrowIfCancellationRequested();

            if (IsDevicePresent())
            {
                stopwatch.Stop();

                logProgress?.Report(new LogEntry($"[DFU] ✓ STM32 Bootloader знайдено за {stopwatch.ElapsedMilliseconds} мс.", Brushes.LimeGreen));
                logProgress?.Report(new LogEntry("[DFU] ✓ Перехід COM → STM32 Bootloader успішний.", Brushes.LimeGreen));

                return true;
            }

            await Task.Delay(100, ct);
        }

        statusProgress?.Report("✖ Збій!");
        logProgress?.Report(new LogEntry("[DFU] ✖ 0483:DF11 не з'явився.", Brushes.IndianRed));

        return false;
    }

    private bool IsDevicePresent()
    {
        try
        {
            // Створюємо свіжий локальний контекст на кожен виклик перевірки
            using var context = new UsbContext();
            using var deviceList = context.List();

            return deviceList.Any(
                d => d.VendorId == StmDfuVid &&
                     d.ProductId == StmDfuPid);
        }
        catch
        {
            return false;
        }
    }

    private static async Task ClearStatusAsync(IUsbDevice device, IProgress<LogEntry>? log = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var setup = new UsbSetupPacket(
            (byte)(
                UsbCtrlFlags.RequestType_Class |
                UsbCtrlFlags.Recipient_Interface |
                UsbCtrlFlags.Direction_Out),
            DFU_CLRSTATUS,
            0,
            0,
            0);

        try
        {
            int written = device.ControlTransfer(setup, Array.Empty<byte>(), 0, 0);
            log?.Report(new LogEntry($"[DFU] CLRSTATUS виконано. Result={written}", Brushes.Gray));
        }
        catch (Exception ex)
        {
            log?.Report(new LogEntry($"[DFU] ⚠ CLRSTATUS: {ex.Message}", Brushes.Orange));
        }

        await Task.Delay(250, ct);
    }

    public async Task FlashFirmwareAsync(string filePath, IProgress<string>? statusProgress = null, IProgress<LogEntry>? logProgress = null, CancellationToken ct = default)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException("Файл прошивки не знайдено.", filePath);

        await Task.Run(async () =>
        {
            ct.ThrowIfCancellationRequested();

            List<HexMemoryBlock> memoryBlocks = await LoadFirmwareAsync(filePath, ct);

            if (memoryBlocks.Count == 0)
                throw new InvalidOperationException("Файл не містить даних для запису.");

            int totalBytes = memoryBlocks.Sum(x => x.Data.Length);
            logProgress?.Report(new LogEntry($"[DFU] Прошивка підготовлена: {totalBytes / 1024.0:F1} КБ, {memoryBlocks.Count} сегментів.", Brushes.DodgerBlue));

            using var context = new UsbContext();
            using var deviceList = context.List();
            var device = deviceList.FirstOrDefault(d => d.VendorId == StmDfuVid && d.ProductId == StmDfuPid);

            if (device == null)
                throw new InvalidOperationException("STM32 Bootloader 0483:DF11 не знайдено.");

            logProgress?.Report(new LogEntry("[DFU] ✓ STM32 Bootloader знайдено.", Brushes.LimeGreen));

            if (!device.IsOpen)
                device.Open();

            try
            {
                device.ClaimInterface(0);
                await Task.Delay(100, ct);

                logProgress?.Report(new LogEntry("[DFU] ✓ USB Interface 0 захоплено.", Brushes.LimeGreen));

                var initialStatus = await GetStatusAsync(device, ct);

                LogStatus(initialStatus, logProgress);

                if (initialStatus.Status != 0)
                {
                    logProgress?.Report(
                        new LogEntry($"[DFU] ⚠ DFU Status={initialStatus.Status}. Виконуємо CLRSTATUS...", Brushes.Orange));

                    var sw = Stopwatch.StartNew();

                    logProgress?.Report(new LogEntry("Початок ClearStatusAsync...", Brushes.Yellow));
                    await ClearStatusAsync(device, logProgress, ct);
                    sw.Stop();
                    logProgress?.Report(new LogEntry($"ClearStatusAsync завершено за: {sw.ElapsedMilliseconds} мс", Brushes.Gold));

                    sw.Restart();
                    logProgress?.Report(new LogEntry("Початок повторного GetStatusAsync...", Brushes.Yellow));
                    initialStatus = await GetStatusAsync(device, ct);
                    sw.Stop();
                    logProgress?.Report(new LogEntry($"GetStatusAsync завершено за: {sw.ElapsedMilliseconds} мс", Brushes.Gold));

                    LogStatus(initialStatus, logProgress);
                }

                if (initialStatus.Status != 0)
                    throw new InvalidOperationException($"DFU помилка: Status={initialStatus.Status}, State={initialStatus.State}");

                if (initialStatus.State != DFU_STATE_DFU_IDLE)
                    throw new InvalidOperationException($"DFU не готовий до запису. Поточний State={initialStatus.State}");

                logProgress?.Report(new LogEntry("[DFU] ✓ DFU знаходиться у стані IDLE.", Brushes.LimeGreen));

                // ====================================================
                // TEST ONLY
                // ====================================================

                if (TestOnly)
                {
                    logProgress?.Report(new LogEntry("[DFU] 🛑 TEST ONLY: Mass Erase пропущено.", Brushes.Gold));
                    logProgress?.Report(new LogEntry("[DFU] 🛑 TEST ONLY: запис Flash пропущено.", Brushes.Gold));

                    statusProgress?.Report("DFU перевірено. Mass Erase і запис не виконувалися.");

                    return;
                }

                #region MASS ERASE
                // ----------------------------------------------------
                // 4. MASS ERASE
                // ----------------------------------------------------

                statusProgress?.Report(
                    "Стирання Flash-пам'яті...");

                logProgress?.Report(
                    new LogEntry(
                        "[DFU] Початок Mass Erase...",
                        Brushes.Orange));

                await ExecuteMassEraseAsync(
                    device,
                    logProgress,
                    ct);

                logProgress?.Report(
                    new LogEntry(
                        "[DFU] ✓ Mass Erase завершено.",
                        Brushes.LimeGreen));

                // ----------------------------------------------------
                // 5. ЗАПИС
                // ----------------------------------------------------

                int writtenBytes = 0;

                // DFU data transfer починається з block #2.
                // Не скидаємо лічильник між сегментами,
                // оскільки це ще потрібно підтвердити для
                // конкретного bootloader.
                ushort blockNumber = 2;

                var progressWatch =
                    Stopwatch.StartNew();

                foreach (var block in memoryBlocks)
                {
                    ct.ThrowIfCancellationRequested();

                    logProgress?.Report(
                        new LogEntry(
                            $"[DFU] Запис сегмента " +
                            $"0x{block.Address:X8}, " +
                            $"{block.Data.Length} байт.",
                            Brushes.LightBlue));

                    // Встановлюємо адресу початку сегмента.
                    await SetAddressAsync(
                        device,
                        block.Address,
                        ct);

                    int offset = 0;

                    while (offset < block.Data.Length)
                    {
                        ct.ThrowIfCancellationRequested();

                        int chunkSize =
                            Math.Min(
                                TransferBlockSize,
                                block.Data.Length - offset);

                        byte[] chunk =
                            new byte[chunkSize];

                        Buffer.BlockCopy(
                            block.Data,
                            offset,
                            chunk,
                            0,
                            chunkSize);

                        await DownloadBlockAsync(
                            device,
                            blockNumber++,
                            chunk,
                            ct);

                        offset += chunkSize;
                        writtenBytes += chunkSize;

                        if (progressWatch.ElapsedMilliseconds > 100 ||
                            writtenBytes == totalBytes)
                        {
                            int percent =
                                writtenBytes * 100 / totalBytes;

                            statusProgress?.Report(
                                $"Запис Flash: " +
                                $"{writtenBytes / 1024} / " +
                                $"{totalBytes / 1024} КБ " +
                                $"({percent}%)");

                            progressWatch.Restart();
                        }
                    }
                }

                logProgress?.Report(
                    new LogEntry(
                        "[DFU] ✓ Запис усіх блоків завершено.",
                        Brushes.LimeGreen));

                // ----------------------------------------------------
                // 6. ФІНАЛІЗАЦІЯ
                // ----------------------------------------------------

                statusProgress?.Report(
                    "Завершення DFU...");

                await LeaveDfuAsync(
                    device,
                    DefaultBaseAddress,
                    ct);

                logProgress?.Report(
                    new LogEntry(
                        "[DFU] ✓ DFU завершено.",
                        Brushes.LimeGreen));
                #endregion
            }
            finally
            {
                try { device.ReleaseInterface(0); } catch { }
                try { if (device.IsOpen) device.Close(); } catch { }
            }
        }, ct);
    }

    private static async Task<List<HexMemoryBlock>> LoadFirmwareAsync(string filePath, CancellationToken ct)
    {
        string extension = Path.GetExtension(filePath).ToLowerInvariant();

        if (extension == ".hex")
        {
            return await HexParser.ParseAsync(filePath, ct);
        }

        if (extension == ".bin")
        {
            byte[] data = await File.ReadAllBytesAsync(filePath, ct);

            if (data.Length == 0)
                throw new InvalidOperationException("BIN-файл порожній.");

            return
            [
                new HexMemoryBlock
                {
                    Address = DefaultBaseAddress,
                    Data = data
                }
            ];
        }

        throw new InvalidOperationException($"Непідтримуваний формат прошивки: {extension}");
    }

    private async Task ExecuteMassEraseAsync(IUsbDevice device, IProgress<LogEntry>? log, CancellationToken ct)
    {
        byte[] command = [0x41];

        log?.Report(new LogEntry("[DFU] Надсилання команди Mass Erase (0x41)...", Brushes.Orange));

        SendControl(device, DFU_DNLOAD, 0, command);

        await WaitForOperationCompleteAsync(device, "Mass Erase", log, ct);
    }

    private async Task SetAddressAsync(IUsbDevice device, uint address, CancellationToken ct)
    {
        byte[] command =
        [
            0x21,
            (byte)(address & 0xFF),
            (byte)((address >> 8) & 0xFF),
            (byte)((address >> 16) & 0xFF),
            (byte)((address >> 24) & 0xFF)
        ];

        SendControl(device, DFU_DNLOAD, 0, command);

        await WaitForOperationCompleteAsync(device, $"Set Address 0x{address:X8}", null, ct);
    }

    private async Task DownloadBlockAsync(IUsbDevice device, ushort blockNumber, byte[] data, CancellationToken ct)
    {
        SendControl(device, DFU_DNLOAD, blockNumber, data);

        await WaitForOperationCompleteAsync(device, $"Запис блоку #{blockNumber}", null, ct);
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
                case DFU_STATE_DNLOAD_SYNC:
                case DFU_STATE_DNLOAD_BUSY:
                    {
                        int delay = Math.Max((int)status.PollTimeoutMs, 10);

                        await Task.Delay(delay, ct);

                        continue;
                    }

                case DFU_STATE_DNLOAD_IDLE:
                case DFU_STATE_DFU_IDLE:
                    return;

                case DFU_STATE_ERROR:
                    throw new InvalidOperationException($"DFU перейшов у ERROR під час '{operation}'.");

                default:
                    throw new InvalidOperationException($"Неочікуваний DFU state {status.State} під час '{operation}'.");
            }
        }
    }

    private static async Task<DfuStatusInfo> GetStatusAsync(IUsbDevice device, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        byte[] buffer = new byte[6];

        var setup =
            new UsbSetupPacket(
                (byte)(
                    UsbCtrlFlags.RequestType_Class |
                    UsbCtrlFlags.Recipient_Interface |
                    UsbCtrlFlags.Direction_In),
                DFU_GETSTATUS, 0, 0, 6);

        int read = await Task.Run(() => device.ControlTransfer(setup, buffer, 0, 6));

        if (read != 6)
            throw new InvalidOperationException($"DFU GETSTATUS повернув {read} байт замість 6.");

        uint pollTimeout =
            (uint)(
                buffer[1] |
                (buffer[2] << 8) |
                (buffer[3] << 16));

        return new DfuStatusInfo(
            buffer[0],
            pollTimeout,
            buffer[4],
            buffer[5]);
    }

    private static void SendControl(IUsbDevice device, byte request, ushort value, byte[] data)
    {
        var setup =
            new UsbSetupPacket(
                (byte)(
                    UsbCtrlFlags.RequestType_Class |
                    UsbCtrlFlags.Recipient_Interface |
                    UsbCtrlFlags.Direction_Out),
                request,
                value,
                0,
                (ushort)data.Length);

        int written = device.ControlTransfer(setup, data, 0, data.Length);

        if (written != data.Length)
        {
            throw new InvalidOperationException($"ControlTransfer: передано {written} з {data.Length} байт.");
        }
    }

    private async Task LeaveDfuAsync(IUsbDevice device, uint startAddress, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        await SetAddressAsync(device, startAddress, ct);

        SendControl(device, DFU_DNLOAD, 0, Array.Empty<byte>());

        try
        {
            await GetStatusAsync(device, ct);
        }
        catch
        { }
    }

    private static void LogStatus(DfuStatusInfo status, IProgress<LogEntry>? log)
    {
        log?.Report(new LogEntry($"[DFU] State={status.State}, Status={status.Status}, PollTimeout={status.PollTimeoutMs}ms", Brushes.Gray));
    }

    public void Dispose()
    {
        _usbContext.Dispose();
        GC.SuppressFinalize(this);
    }
}