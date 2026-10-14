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
            await Task.Delay(700);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[DFU] RebootToBootloaderAsync: {ex.Message}");
        }
    }

    public async Task<bool> EnterBootloaderAndWaitAsync(string? portName, IProgress<string>? statusProgress = null, IProgress<LogEntry>? logProgress = null, CancellationToken ct = default)
    {
        statusProgress?.Report("Налаштування пристрою...");

        if (!string.IsNullOrWhiteSpace(portName))
        {
            logProgress?.Report(new LogEntry($"[DFU] Переведення {portName} у Bootloader...", LogLevel.Warning));
            await RebootToBootloaderAsync(portName);
        }
        else
        {
            logProgress?.Report(new LogEntry("[DFU] Очікування безпосереднього підключення DFU...", LogLevel.Info));
        }

        statusProgress?.Report("Очікування STM32 DFU Bootloader...");
        logProgress?.Report(new LogEntry("[DFU] Очікування USB re-enumeration 0483:DF11...", LogLevel.Info));

        var stopwatch = Stopwatch.StartNew();
        const int timeoutMs = 10000;
        const int pollIntervalMs = 100;

        while (stopwatch.ElapsedMilliseconds < timeoutMs)
        {
            ct.ThrowIfCancellationRequested();

            if (IsDevicePresent())
            {
                stopwatch.Stop();

                logProgress?.Report(new LogEntry($"[DFU] ✓ STM32 Bootloader 0483:DF11 знайдено за {stopwatch.ElapsedMilliseconds} мс.", LogLevel.Success));
                logProgress?.Report(new LogEntry("[DFU] ✓ COM → DFU USB re-enumeration успішна.", LogLevel.Success));
                statusProgress?.Report("STM32 DFU Bootloader знайдено.");

                return true;
            }

            await Task.Delay(pollIntervalMs, ct);
        }

        stopwatch.Stop();

        statusProgress?.Report("✖ DFU Bootloader не знайдено.");
        logProgress?.Report(new LogEntry($"[DFU] ✖ 0483:DF11 не з'явився за {stopwatch.ElapsedMilliseconds} мс.", LogLevel.Error));
        logProgress?.Report(new LogEntry("[DFU] Перевірте USB-кабель, живлення та драйвер WinUSB.", LogLevel.Warning));

        return false;
    }

    private static async Task ClearStatusAsync(IUsbDevice device, IProgress<LogEntry>? log = null, CancellationToken ct = default)
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

    public async Task<bool> FlashFirmwareAsync(string filePath, IProgress<string>? statusProgress = null, IProgress<LogEntry>? logProgress = null, CancellationToken ct = default)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException("Файл прошивки не знайдено.", filePath);

        return await Task.Run(async () =>
        {
            ct.ThrowIfCancellationRequested();

            List<HexMemoryBlock> memoryBlocks = await LoadFirmwareAsync(filePath, ct);

            if (memoryBlocks.Count == 0)
                throw new InvalidOperationException("Файл не містить даних для запису.");

            int totalBytes = memoryBlocks.Sum(x => x.Data.Length);
            logProgress?.Report(new LogEntry($"[DFU] Прошивка підготовлена: {totalBytes / 1024.0:F1} КБ, {memoryBlocks.Count} сегментів.", Brushes.DodgerBlue));

            using var context = new UsbContext();
            using var deviceList = context.List();
            var device = deviceList.FirstOrDefault(d => d.VendorId == DfuConstants.StmDfuVid && d.ProductId == DfuConstants.StmDfuPid);

            if (device == null)
                throw new InvalidOperationException("STM32 Bootloader 0483:DF11 не знайдено.");

            logProgress?.Report(new LogEntry("[DFU] ✓ STM32 Bootloader знайдено.", LogLevel.Success));

            if (!device.IsOpen)
                device.Open();

            bool interfaceClaimed = false;

            try
            {
                if (!device.ClaimInterface(0))
                {
                    throw new InvalidOperationException("[DFU] Не вдалося захопити DFU interface 0.");
                }
                interfaceClaimed = true;

                await Task.Delay(100, ct);

                logProgress?.Report(new LogEntry("[DFU] ✓ USB Interface 0 захоплено.", LogLevel.Success));
                logProgress?.Report(new LogEntry("[DFU] CLRSTATUS → примусове очищення...", LogLevel.Warning));

                await ClearStatusAsync(device, logProgress, ct);

                var statusWatch = Stopwatch.StartNew();

                logProgress?.Report(new LogEntry("[DFU] GETSTATUS → запит...", LogLevel.Gray));

                var initialStatus = await GetStatusAsync(device, ct);
                statusWatch.Stop();

                logProgress?.Report(new LogEntry($"[DFU] GETSTATUS ← {statusWatch.ElapsedMilliseconds} мс: State={initialStatus.State}, Status={initialStatus.Status}, PollTimeout={initialStatus.PollTimeoutMs} мс", LogLevel.Gray));
                LogStatus(initialStatus, logProgress);


                if (initialStatus.Status != 0)
                {
                    logProgress?.Report(new LogEntry($"[DFU] ⚠ DFU Status={initialStatus.Status}, State={initialStatus.State}. Очищення помилки...", LogLevel.Warning));

                    await ClearStatusAsync(device, logProgress, ct);

                    initialStatus = await GetStatusAsync(device, ct);
                    LogStatus(initialStatus, logProgress);
                }

                if (initialStatus.Status != 0)
                    throw new InvalidOperationException($"DFU помилка: Status={initialStatus.Status}, State={initialStatus.State}");

                if (initialStatus.State != DfuConstants.DFU_STATE_DFU_IDLE)
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

                    await LeaveDfuAsync(device, logProgress, ct);

                    //return;
                }
                return false;

                /*
                #region MASS ERASE
                // ----------------------------------------------------
                // 4. MASS ERASE
                // ----------------------------------------------------

                statusProgress?.Report("Стирання Flash-пам'яті...");
                logProgress?.Report(new LogEntry("[DFU] Початок Mass Erase...", Brushes.Orange));

                await ExecuteMassEraseAsync(device, logProgress, ct);

                logProgress?.Report(new LogEntry("[DFU] ✓ Mass Erase завершено, DFU готовий до запису.", Brushes.LimeGreen));

                // ----------------------------------------------------
                // 5. ЗАПИС
                // ----------------------------------------------------

                int writtenBytes = 0;
                var progressWatch = Stopwatch.StartNew();

                foreach (var block in memoryBlocks)
                {
                    ct.ThrowIfCancellationRequested();

                    logProgress?.Report(new LogEntry($"[DFU] Запис сегмента 0x{block.Address:X8}, {block.Data.Length} байт.", Brushes.LightBlue));

                    // Встановлюємо адресу початку сегмента.
                    await SetAddressAsync(device, block.Address, ct);

                    // GPT ПРАВИЙ: Для кожного нового сегмента нумерація блоків МАЄ починатися з 2!
                    ushort blockNumber = 2;
                    int offset = 0;

                    while (offset < block.Data.Length)
                    {
                        ct.ThrowIfCancellationRequested();

                        int chunkSize = Math.Min(TransferBlockSize, block.Data.Length - offset);
                        byte[] chunk = new byte[chunkSize];
                        Buffer.BlockCopy(block.Data, offset, chunk, 0, chunkSize);

                        await DownloadBlockAsync(device, blockNumber++, chunk, ct);

                        offset += chunkSize;
                        writtenBytes += chunkSize;

                        if (progressWatch.ElapsedMilliseconds > 100 || writtenBytes == totalBytes)
                        {
                            int percent = writtenBytes * 100 / totalBytes;
                            statusProgress?.Report($"Запис Flash: {writtenBytes / 1024} / {totalBytes / 1024} КБ ({percent}%)");
                            progressWatch.Restart();
                        }
                    }
                }

                // ----------------------------------------------------
                // 6. ФІНАЛІЗАЦІЯ
                // ----------------------------------------------------

                statusProgress?.Report("Завершення DFU...");

                await LeaveDfuAsync(device, logProgress, ct);

                logProgress?.Report(new LogEntry("[DFU] ✓ DFU завершено.", Brushes.LimeGreen));

                return true;
                #endregion*/
            }
            finally
            {
                if (interfaceClaimed)
                {
                    try
                    {
                        device.ReleaseInterface(0);
                    }
                    catch
                    {
                    }
                }

                try
                {
                    if (device.IsOpen)
                        device.Close();
                }
                catch
                {
                }
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
                    Address = DfuConstants.DefaultBaseAddress,
                    Data = data
                }
            ];
        }

        throw new InvalidOperationException($"Непідтримуваний формат прошивки: {extension}");
    }

    private async Task ExecuteMassEraseAsync(IUsbDevice device, IProgress<LogEntry>? log, CancellationToken ct)
    {
        byte[] command = [0x41];

        log?.Report(new LogEntry("[DFU] Надсилання команди Mass Erase (0x41)...", LogLevel.Warning));

        await SendControlAsync(device, DfuConstants.DFU_DNLOAD, 0, command, ct);
        await WaitForOperationCompleteAsync(device, "Mass Erase", log, ct);

        DfuStatusInfo status = await GetStatusAsync(device, ct);

        log?.Report(new LogEntry($"[DFU] Стан після Mass Erase: State={status.State}, Status={status.Status}", LogLevel.Gray));

        if (status.Status != 0)
        {
            throw new InvalidOperationException($"[DFU] Помилка після Mass Erase: Status={status.Status}, State={status.State}");
        }

        if (status.State != DfuConstants.DFU_STATE_DNLOAD_IDLE &&
            status.State != DfuConstants.DFU_STATE_DFU_IDLE)
        {
            throw new InvalidOperationException($"[DFU] Неочікуваний стан після Mass Erase: State={status.State}");
        }

        log?.Report(new LogEntry("[DFU] ✓ Mass Erase завершено, стан DFU перевірено.", LogLevel.Success));
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

        await SendControlAsync(device, DfuConstants.DFU_DNLOAD, 0, command, ct);
        await WaitForOperationCompleteAsync(device, $"Set Address 0x{address:X8}", null, ct);
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
                    {
                        int delay = Math.Max((int)status.PollTimeoutMs, 10);
                        await Task.Delay(delay, ct);

                        continue;
                    }

                case DfuConstants.DFU_STATE_DNLOAD_IDLE:
                case DfuConstants.DFU_STATE_DFU_IDLE:
                    return;

                case  DfuConstants.DFU_STATE_ERROR:
                    throw new InvalidOperationException($"DFU перейшов у ERROR під час '{operation}'.");

                default:
                    throw new InvalidOperationException($"Неочікуваний DFU state {status.State} під час '{operation}'.");
            }
        }
    }

    private static async Task<DfuStatusInfo> GetStatusAsync(IUsbDevice device, CancellationToken ct)
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
            {
                throw new IOException($"DFU GETSTATUS повернув {read} байт замість {statusLength}.");
            }

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

    private static async Task SendControlAsync(IUsbDevice device, byte request, ushort value, byte[] data, CancellationToken ct)
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
        {
            throw new IOException($"ControlTransfer: передано {written} з {data.Length} байт.");
        }
    }

    private async Task LeaveDfuAsync(IUsbDevice device, IProgress<LogEntry>? log, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        await SendControlAsync(device, DfuConstants.DFU_DNLOAD, 0, Array.Empty<byte>(), ct);

        log?.Report(new LogEntry("[DFU] Команду виходу з DFU надіслано.", LogLevel.Info));

        try
        {
            DfuStatusInfo status = await GetStatusAsync(device, ct);

            if (status.Status != 0)
            {
                throw new InvalidOperationException($"[DFU] Помилка під час виходу: Status={status.Status}, State={status.State}");
            }

            log?.Report(new LogEntry($"[DFU] Відповідь на вихід отримано: State={status.State}, Status={status.Status}.", LogLevel.Success));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            if (IsDevicePresent())
            {
                throw new IOException("[DFU] Не вдалося підтвердити вихід із DFU, а пристрій усе ще присутній.", ex);
            }

            log?.Report(new LogEntry("[DFU] Пристрій зник з USB після команди виходу. Перезапуск слід підтвердити повторним виявленням.", LogLevel.Warning));
        }
    }

    #region Helpers
    private async Task DownloadBlockAsync(IUsbDevice device, ushort blockNumber, byte[] data, CancellationToken ct)
    {
        await SendControlAsync(device, DfuConstants.DFU_DNLOAD, blockNumber, data, ct);
        await WaitForOperationCompleteAsync(device, $"Запис блоку #{blockNumber}", null, ct);
    }
    private bool IsDevicePresent()
    {
        try
        {
            using var context = new UsbContext();
            using var deviceList = context.List();

            return deviceList.Any(
                d => d.VendorId == DfuConstants.StmDfuVid &&
                     d.ProductId == DfuConstants.StmDfuPid);
        }
        catch
        {
            return false;
        }
    }

    private static void LogStatus(DfuStatusInfo status, IProgress<LogEntry>? log)
    {
        log?.Report(new LogEntry($"[DFU] State={status.State}, Status={status.Status}, PollTimeout={status.PollTimeoutMs}ms", Brushes.Gray));
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }
    #endregion
}