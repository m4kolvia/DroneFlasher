using System.Diagnostics;
using System.IO;
using System.Windows.Media;
using DroneFlasher.Constants;
using DroneFlasher.Models;
using LibUsbDotNet.LibUsb;

namespace DroneFlasher.Services.Firmware;

public class DfuService
{
    private readonly IDfuUsbClient _usbClient;

    public DfuService(IDfuUsbClient? usbClient = null)
    {
        _usbClient = usbClient ?? new DfuUsbClient();
    }

    public async Task<bool> EnterBootloaderAndWaitAsync(string? portName, IProgress<string>? statusProgress = null, IProgress<LogEntry>? logProgress = null, CancellationToken ct = default)
    {
        statusProgress?.Report("Налаштування пристрою...");

        if (!string.IsNullOrWhiteSpace(portName))
        {
            logProgress?.Report(new LogEntry($"[DFU] Переведення {portName} у Bootloader...", LogLevel.Warning));
            await SerialBootloaderTrigger.RebootToBootloaderAsync(portName);
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

            if (_usbClient.IsDevicePresent())
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

    public async Task<bool> FlashFirmwareAsync(string filePath, IProgress<string>? statusProgress = null, IProgress<LogEntry>? logProgress = null, CancellationToken ct = default)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException("Файл прошивки не знайдено.", filePath);

        return await Task.Run(async () =>
        {
            ct.ThrowIfCancellationRequested();

            var memoryBlocks = await FirmwareLoader.LoadAsync(filePath, ct);

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
                    throw new InvalidOperationException("[DFU] Не вдалося захопити DFU interface 0.");

                interfaceClaimed = true;
                await Task.Delay(100, ct);

                logProgress?.Report(new LogEntry("[DFU] ✓ USB Interface 0 захоплено.", LogLevel.Success));
                logProgress?.Report(new LogEntry("[DFU] CLRSTATUS → примусове очищення...", LogLevel.Warning));

                await _usbClient.ClearStatusAsync(device, logProgress, ct);
                var statusWatch = Stopwatch.StartNew();
                logProgress?.Report(new LogEntry("[DFU] GETSTATUS → запит...", LogLevel.Gray));

                var initialStatus = await _usbClient.GetStatusAsync(device, ct);
                statusWatch.Stop();

                logProgress?.Report(new LogEntry($"[DFU] GETSTATUS ← {statusWatch.ElapsedMilliseconds} мс: State={initialStatus.State}, Status={initialStatus.Status}, PollTimeout={initialStatus.PollTimeoutMs} мс", LogLevel.Gray));

                if (initialStatus.Status != 0)
                {
                    logProgress?.Report(new LogEntry($"[DFU] ⚠ DFU Status={initialStatus.Status}, State={initialStatus.State}. Очищення помилки...", LogLevel.Warning));
                    await _usbClient.ClearStatusAsync(device, logProgress, ct);
                    initialStatus = await _usbClient.GetStatusAsync(device, ct);
                }

                if (initialStatus.Status != 0)
                    throw new InvalidOperationException($"DFU помилка: Status={initialStatus.Status}, State={initialStatus.State}");

                if (initialStatus.State != DfuConstants.DFU_STATE_DFU_IDLE)
                    throw new InvalidOperationException($"DFU не готовий до запису. Поточний State={initialStatus.State}");

                logProgress?.Report(new LogEntry("[DFU] ✓ DFU знаходиться у стані IDLE.", Brushes.LimeGreen));
                statusProgress?.Report("Стирання Flash-пам'яті...");
                logProgress?.Report(new LogEntry("[DFU] Початок Mass Erase...", Brushes.Orange));

                await _usbClient.ExecuteMassEraseAsync(device, logProgress, ct);

                logProgress?.Report(new LogEntry("[DFU] ✓ Mass Erase завершено, DFU готовий до запису.", Brushes.LimeGreen));

                int writtenBytes = 0;
                var progressWatch = Stopwatch.StartNew();

                foreach (var block in memoryBlocks)
                {
                    ct.ThrowIfCancellationRequested();
                    logProgress?.Report(new LogEntry($"[DFU] Запис сегмента 0x{block.Address:X8}, {block.Data.Length} байт.", Brushes.LightBlue));

                    await _usbClient.SetAddressAsync(device, block.Address, ct);

                    ushort blockNumber = 2;
                    int offset = 0;

                    while (offset < block.Data.Length)
                    {
                        ct.ThrowIfCancellationRequested();

                        int chunkSize = Math.Min(DfuConstants.TransferBlockSize, block.Data.Length - offset);
                        byte[] chunk = new byte[chunkSize];
                        Buffer.BlockCopy(block.Data, offset, chunk, 0, chunkSize);

                        await _usbClient.DownloadBlockAsync(device, blockNumber++, chunk, ct);

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

                statusProgress?.Report("Запуск прошивки...");
                logProgress?.Report(new LogEntry("[DFU] Перезавантаження контролера...", Brushes.LightBlue));

                await _usbClient.LeaveDfuAsync(device, logProgress, ct);

                statusProgress?.Report("Готово!");
                logProgress?.Report(new LogEntry("[DFU] ✓ Прошивання успішно завершено!", Brushes.LimeGreen));

                return true;
            }
            finally
            {
                if (interfaceClaimed)
                {
                    try { device.ReleaseInterface(0); } catch { }
                }
                try { if (device.IsOpen) device.Close(); } catch { }
            }
        }, ct);
    }
}