using DroneFlasher.Models;
using DroneFlasher.Services;
using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace DroneFlasher.ViewModels;

public class MainViewModel : BaseViewModel
{
    private readonly SerialService _serialService = new();
    private readonly DfuService _dfuService = new();
    public ICommand CopyLogCommand { get; }
    public ICommand TestDfuCommand { get; }
    public ICommand SelectDumpCommand { get; }
    public ICommand SelectFirmwareCommand { get; }
    public ICommand ManualFlashCommand { get; }
    public ICommand ClearLogCommand { get; }

    private string? _justFinishedPort;

    private readonly DispatcherTimer _scanTimer;

    private string[] _knownPorts = [];
    private string[] _lastSystemPorts = [];

    private string _dumpPath = string.Empty;
    private string _firmwarePath = string.Empty;

    private bool _isAutoFlashEnabled;

    private string _statusText =
        "Очікування налаштувань...";

    private Brush _statusBrush =
        Brushes.Gray;

    private bool _isBusy;

    public bool IsBusy
    {
        get => _isBusy;

        set
        {
            if (SetField(ref _isBusy, value))
            {
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public string DumpPath
    {
        get => _dumpPath;

        set => SetField(
            ref _dumpPath,
            value);
    }

    public string FirmwarePath
    {
        get => _firmwarePath;

        set => SetField(
            ref _firmwarePath,
            value);
    }

    public bool IsAutoFlashEnabled
    {
        get => _isAutoFlashEnabled;

        set
        {
            if (!SetField(
                    ref _isAutoFlashEnabled,
                    value))
            {
                return;
            }

            if (value)
            {
                UpdateStatus(
                    "Пошук вже підключеного дрона...",
                    Brushes.DodgerBlue);

                // ВАЖЛИВО:
                // одразу перевіряємо, чи дрон уже підключений.
                _ = TryTriggerPipelineForAlreadyConnectedDeviceAsync();
            }
            else
            {
                UpdateStatus(
                    "Автозавантаження вимкнено",
                    Brushes.Gray);
            }
        }
    }

    public string StatusText
    {
        get => _statusText;

        set => SetField(
            ref _statusText,
            value);
    }

    public Brush StatusBrush
    {
        get => _statusBrush;

        set => SetField(
            ref _statusBrush,
            value);
    }

    public ObservableCollection<LogEntry> LogEntries { get; } = new();

    public MainViewModel()
    {
        CopyLogCommand =
    new RelayCommand(
        _ => CopyEntireLog());

        SelectDumpCommand =
            new RelayCommand(
                _ => SelectFile(
                    p => DumpPath = p,
                    "TXT files (*.txt)|*.txt"));

        SelectFirmwareCommand =
            new RelayCommand(
                _ => SelectFile(
                    p => FirmwarePath = p,
                    "HEX/BIN files (*.hex;*.bin)|*.hex;*.bin"));

        ManualFlashCommand =
            new RelayCommand(
                async _ =>
                    await ExecuteManualFlashAsync(),
                _ => !IsBusy);

        TestDfuCommand =
            new RelayCommand(
                async _ =>
                    await ExecuteTestDfuAsync(),
                _ => !IsBusy);

        ClearLogCommand =
            new RelayCommand(
                _ => LogEntries.Clear());

        try
        {
            _knownPorts =
                DeviceWatcherService
                    .GetFlightControllerPorts()
                    .ToArray();

            _lastSystemPorts =
                SerialPort.GetPortNames();
        }
        catch
        {
            _knownPorts = [];
            _lastSystemPorts = [];
        }

        _scanTimer =
            new DispatcherTimer
            {
                Interval =
                    TimeSpan.FromMilliseconds(500)
            };

        _scanTimer.Tick +=
            ScanTimer_Tick;

        _scanTimer.Start();
    }

    private void CopyEntireLog()
    {

            string text =
                string.Join(
                    Environment.NewLine,
                    LogEntries.Select(
                        entry => entry.Text));

            Clipboard.SetText(text);

        

    }

    // ================================================================
    // ПОШУК ВЖЕ ПІДКЛЮЧЕНОГО ДРОНА
    // ================================================================

    private async Task TryTriggerPipelineForAlreadyConnectedDeviceAsync()
    {
        if (IsBusy ||
            !IsAutoFlashEnabled)
        {
            return;
        }

        bool hasFirmware =
            !string.IsNullOrWhiteSpace(FirmwarePath) &&
            File.Exists(FirmwarePath);

        bool hasDump =
            !string.IsNullOrWhiteSpace(DumpPath) &&
            File.Exists(DumpPath);

        if (!hasFirmware && !hasDump)
        {
            UpdateStatus(
                "Оберіть прошивку або конфігурацію.",
                Brushes.Orange);

            return;
        }

        // ------------------------------------------------------------
        // 1. Спочатку перевіряємо Flight Controller через
        //    DeviceWatcherService.
        // ------------------------------------------------------------

        var existingPorts =
            DeviceWatcherService
                .GetFlightControllerPorts()
                .ToArray();

        if (existingPorts.Length > 0)
        {
            string targetPort =
                existingPorts[0];

            if (targetPort != _justFinishedPort)
            {
                _scanTimer.Stop();

                try
                {
                    await ExecutePipelineAsync(
                        targetPort);
                }
                finally
                {
                    ResetKnownPorts();

                    await Task.Delay(1000);

                    if (IsAutoFlashEnabled)
                        _scanTimer.Start();
                }

                return;
            }
        }

        // ------------------------------------------------------------
        // 2. Якщо DeviceWatcher не знайшов FC,
        //    перевіряємо всі COM-порти через CLI.
        // ------------------------------------------------------------

        if (hasDump)
        {
            UpdateStatus(
                "Пошук польотного контролера серед COM-портів...",
                Brushes.DodgerBlue);

            string? foundPort = null;

            try
            {
                foundPort =
                    await _serialService
                        .FindFlightControllerPortAsync();
            }
            catch
            {
                // Якщо не знайшли — нижче продовжиться
                // звичайне автоматичне сканування.
            }

            if (!string.IsNullOrEmpty(foundPort) &&
                foundPort != _justFinishedPort)
            {
                _scanTimer.Stop();

                try
                {
                    AddLog(
                        $"[AUTO] Знайдено польотний контролер: {foundPort}",
                        Brushes.LightGreen);

                    await ExecutePipelineAsync(
                        foundPort);
                }
                finally
                {
                    ResetKnownPorts();

                    await Task.Delay(1000);

                    if (IsAutoFlashEnabled)
                        _scanTimer.Start();
                }

                return;
            }
        }

        // ------------------------------------------------------------
        // 3. Якщо є прошивка — перевіряємо DFU.
        // ------------------------------------------------------------

        if (hasFirmware)
        {
            bool inDfu = false;

            try
            {
                inDfu =
                    await _dfuService
                        .IsDeviceInDfuModeAsync();
            }
            catch
            {
            }

            if (inDfu)
            {
                _scanTimer.Stop();

                try
                {
                    await ExecutePipelineAsync(
                        portName: null);
                }
                finally
                {
                    ResetKnownPorts();

                    await Task.Delay(2000);

                    if (IsAutoFlashEnabled)
                        _scanTimer.Start();
                }
            }
        }
    }
    // ================================================================
    // ТЕСТ DFU
    // ================================================================

    private async Task ExecuteTestDfuAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        LogEntries.Clear();

        try
        {
            // 1. Пошук COM-порту
            UpdateStatus("Пошук COM-порту...", Brushes.Orange);
            var ports = DeviceWatcherService.GetFlightControllerPorts().ToArray();

            if (ports.Length == 0)
            {
                UpdateStatus("✖ COM-порт не знайдено", Brushes.IndianRed);
                AddLog("[TEST] Немає підключених COM-портів для ребуту.", Brushes.IndianRed);
                return;
            }

            string targetPort = ports[0];
            AddLog($"[TEST] Знайдено COM-порт: {targetPort}", Brushes.LightGreen);

            // 2. Вхід у Bootloader
            UpdateStatus($"Переведення {targetPort} у Bootloader...", Brushes.Orange);
            AddLog($"[TEST] Надсилаємо команду в {targetPort}...", Brushes.Orange);

            try
            {
                await _dfuService.RebootToBootloaderAsync(targetPort);
            }
            catch
            {
                // Обрив зв'язку при стрибку в бутлоадер — це штатна поведінка
            }

            AddLog("[TEST] Команду надіслано. Очікування перемикання USB шини...", Brushes.LightBlue);

            // 3. Очікування появи пристрою в системі
            bool inDfu = false;
            for (int i = 0; i < 25; i++)
            {
                await Task.Delay(400);
                inDfu = await _dfuService.IsDeviceInDfuModeAsync();
                if (inDfu) break;
            }

            if (!inDfu)
            {
                UpdateStatus("✖ Пристрій не перейшов у DFU", Brushes.IndianRed);
                AddLog("[TEST] Пристрій 0483:DF11 не знайдено серед PnP пристроїв.", Brushes.IndianRed);
                return;
            }

            AddLog("[TEST] ✓ STM32 Bootloader зафіксовано в системі!", Brushes.LimeGreen);

            // 4. Перевірка доступності через LibUsbDotNet
            UpdateStatus("Перевірка доступу до DFU...", Brushes.Orange);
            var progress = new Progress<string>(msg => AddLog(msg, Brushes.Gray));

            var statusInfo = await _dfuService.QueryDfuStatusAsync(progress);

            if (statusInfo.State == 2) // STATE_DFU_IDLE
            {
                UpdateStatus("✔ DFU готовий до роботи (IDLE)", Brushes.LimeGreen);
                AddLog($"[TEST] УСПІХ: Стан {statusInfo.State} (dfuIDLE). Плата готова до прошивки!", Brushes.LimeGreen);
            }
            else
            {
                UpdateStatus($"⚠ DFU стан: {statusInfo.State}", Brushes.Orange);
                AddLog($"[TEST] Стан: {statusInfo.State}, статус: {statusInfo.Status}.", Brushes.Orange);
            }
        }
        catch (Exception ex)
        {
            UpdateStatus($"Збій тесту: {ex.Message}", Brushes.IndianRed);
            AddLog($"[TEST] Помилка доступу до DFU: {ex.Message}", Brushes.IndianRed);
        }
        finally
        {
            IsBusy = false;
        }
    }

    // ================================================================
    // РУЧНИЙ ЗАПУСК
    // ================================================================

    private async Task ExecuteManualFlashAsync()
    {
        if (IsBusy)
            return;

        _scanTimer.Stop();

        try
        {
            _justFinishedPort = null;

            var ports =
                DeviceWatcherService
                    .GetFlightControllerPorts()
                    .ToArray();

            if (ports.Length > 0)
            {
                await ExecutePipelineAsync(
                    ports.First());

                return;
            }

            // Додатковий пошук через CLI.
            if (!string.IsNullOrWhiteSpace(DumpPath))
            {
                string? foundPort =
                    await _serialService
                        .FindFlightControllerPortAsync();

                if (!string.IsNullOrEmpty(foundPort))
                {
                    await ExecutePipelineAsync(
                        foundPort);

                    return;
                }
            }

            // Перевірка DFU.
            if (!string.IsNullOrWhiteSpace(FirmwarePath) &&
                await _dfuService.IsDeviceInDfuModeAsync())
            {
                await ExecutePipelineAsync(
                    portName: null);

                return;
            }

            UpdateStatus(
                "Помилка: дрон не знайдено (перевірте USB або COM-порт)!",
                Brushes.IndianRed);
        }
        finally
        {
            ResetKnownPorts();

            await Task.Delay(500);

            if (IsAutoFlashEnabled)
                _scanTimer.Start();
        }
    }

    // ================================================================
    // ВИБІР ФАЙЛУ
    // ================================================================

    private void SelectFile(
        Action<string> setPath,
        string filter)
    {
        var dlg =
            new OpenFileDialog
            {
                Filter = filter
            };

        if (dlg.ShowDialog() == true)
        {
            setPath(
                dlg.FileName);
        }
    }

    // ================================================================
    // АВТОМАТИЧНЕ СКАНУВАННЯ
    // ================================================================

    private async void ScanTimer_Tick(
        object? sender,
        EventArgs e)
    {
        if (IsBusy ||
            !IsAutoFlashEnabled)
        {
            return;
        }

        // ------------------------------------------------------------
        // 1. Спочатку DFU
        // ------------------------------------------------------------

        if (!string.IsNullOrWhiteSpace(FirmwarePath) &&
            File.Exists(FirmwarePath))
        {
            bool inDfu = false;

            try
            {
                inDfu =
                    await _dfuService
                        .IsDeviceInDfuModeAsync();
            }
            catch
            {
            }

            if (inDfu)
            {
                _scanTimer.Stop();

                try
                {
                    await ExecutePipelineAsync(
                        portName: null);
                }
                finally
                {
                    ResetKnownPorts();

                    await Task.Delay(2000);

                    if (IsAutoFlashEnabled)
                        _scanTimer.Start();
                }

                return;
            }
        }

        // ------------------------------------------------------------
        // 2. Звичайні COM-порти
        // ------------------------------------------------------------

        string[] currentRawPorts;

        try
        {
            currentRawPorts =
                SerialPort.GetPortNames();
        }
        catch
        {
            return;
        }

        // Якщо це той самий порт, який щойно завершив роботу,
        // не запускаємо його повторно.
        if (!string.IsNullOrEmpty(_justFinishedPort))
        {
            if (currentRawPorts.Contains(
                    _justFinishedPort,
                    StringComparer.OrdinalIgnoreCase))
            {
                return;
            }

            _justFinishedPort = null;

            UpdateStatus(
                "Готовий до підключення нового борту...",
                Brushes.DodgerBlue);
        }

        var currentPorts =
            DeviceWatcherService
                .GetFlightControllerPorts()
                .ToArray();

        var newPorts =
            currentPorts
                .Except(
                    _knownPorts,
                    StringComparer.OrdinalIgnoreCase)
                .ToArray();

        _knownPorts =
            currentPorts;

        _lastSystemPorts =
            currentRawPorts;

        if (newPorts.Length > 0)
        {
            string detectedPort =
                newPorts[0];

            _scanTimer.Stop();

            try
            {
                await ExecutePipelineAsync(
                    detectedPort);
            }
            finally
            {
                ResetKnownPorts();

                await Task.Delay(1000);

                if (IsAutoFlashEnabled)
                    _scanTimer.Start();
            }
        }
    }

    // ================================================================
    // ОНОВЛЕННЯ СПИСКУ ПОРТІВ
    // ================================================================

    private void ResetKnownPorts()
    {
        try
        {
            _knownPorts =
                DeviceWatcherService
                    .GetFlightControllerPorts()
                    .ToArray();

            _lastSystemPorts =
                SerialPort.GetPortNames();
        }
        catch
        {
            _knownPorts = [];
            _lastSystemPorts = [];
        }
    }

    // ================================================================
    // ОСНОВНИЙ PIPELINE
    // ================================================================

    private async Task ExecutePipelineAsync(
     string? portName)
    {
        IsBusy = true;

        LogEntries.Clear();

        try
        {
            bool hasFirmware =
                !string.IsNullOrWhiteSpace(FirmwarePath) &&
                File.Exists(FirmwarePath);

            bool hasDump =
                !string.IsNullOrWhiteSpace(DumpPath) &&
                File.Exists(DumpPath);

            if (!hasFirmware && !hasDump)
            {
                UpdateStatus(
                    "Помилка: не обрано файл конфігурації або прошивки!",
                    Brushes.IndianRed);

                return;
            }

            var progress =
                new Progress<string>(
                    msg =>
                        UpdateStatus(
                            msg,
                            Brushes.Orange));

            // ============================================================
            // 1. ПРОШИВКА (Тимчасово вимкнено для тесту переходу в DFU)
            // ============================================================

            if (hasFirmware)
            {
                if (!string.IsNullOrEmpty(portName))
                {
                    UpdateStatus($"Переведення {portName} у Bootloader...", Brushes.Orange);
                    AddLog($"[DFU] Переведення {portName} у Bootloader...", Brushes.Orange);

                    await _dfuService.RebootToBootloaderAsync(portName);
                    AddLog("[DFU] Команду bl відправлено.", Brushes.LightBlue);
                }

                UpdateStatus("Очікування STM32 BOOTLOADER...", Brushes.Orange);
                bool inDfu = false;

                for (int i = 0; i < 30; i++)
                {
                    await Task.Delay(250);
                    try
                    {
                        inDfu = await _dfuService.IsDeviceInDfuModeAsync();
                        if (inDfu) break;
                    }
                    catch { }
                }

                foreach (string line in _dfuService.GetConnectedDevicesLog())
                {
                    AddLog(line, Brushes.Gray);
                }

                if (!inDfu)
                {
                    throw new InvalidOperationException("STM32 BOOTLOADER не з'явився.");
                }

                AddLog("[DFU] ✓ STM32 BOOTLOADER знайдено.", Brushes.LimeGreen);
                UpdateStatus("✔ STM32 BOOTLOADER знайдено (пропуск прошивки)", Brushes.LimeGreen);

                // Завершуємо крок без виклику відсутнього CubeProgrammer
                return;
            }
            // ============================================================
            // 2. КОНФІГУРАЦІЯ
            // ============================================================

            if (hasDump)
            {
                if (string.IsNullOrEmpty(portName))
                {
                    portName =
                        await WaitForNewPortAsync(
                            timeoutSeconds: 5);

                    if (string.IsNullOrEmpty(portName))
                    {
                        portName =
                            DeviceWatcherService
                                .GetFlightControllerPorts()
                                .FirstOrDefault();
                    }
                }

                // Остання спроба — прямий пошук FC.
                if (string.IsNullOrEmpty(portName))
                {
                    portName =
                        await _serialService
                            .FindFlightControllerPortAsync();
                }

                if (string.IsNullOrEmpty(portName))
                {
                    throw new InvalidOperationException(
                        "Не знайдено активний COM-порт " +
                        "для налаштування!");
                }

                var logProgress =
                    new Progress<LogEntry>(
                        entry =>
                        {
                            LogEntries.Add(entry);

                            if (LogEntries.Count > 1500)
                            {
                                LogEntries.RemoveAt(0);
                            }
                        });

                await _serialService
                    .ApplyDumpAsync(
                        portName,
                        DumpPath,
                        progress,
                        logProgress);
            }

            // ============================================================
            // 3. ГОТОВО
            // ============================================================

            UpdateStatus(
                "ГОТОВО! ПІДКЛЮЧІТЬ НАСТУПНИЙ",
                Brushes.LimeGreen);

            _justFinishedPort =
                portName;
        }
        catch (Exception ex)
        {
            UpdateStatus(
                $"ЗБІЙ: {ex.Message}",
                Brushes.IndianRed);

            AddLog(
                $"[PIPELINE] ✖ {ex.Message}",
                Brushes.IndianRed);

            _justFinishedPort = null;
        }
        finally
        {
            IsBusy = false;
        }
    }

    // ================================================================
    // ОЧІКУВАННЯ COM
    // ================================================================

    private async Task<string?> WaitForNewPortAsync(
        int timeoutSeconds)
    {
        for (
            int i = 0;
            i < timeoutSeconds * 2;
            i++)
        {
            var ports =
                DeviceWatcherService
                    .GetFlightControllerPorts()
                    .ToArray();

            if (ports.Length > 0)
            {
                return ports.Last();
            }

            await Task.Delay(500);
        }

        return null;
    }

    // ================================================================
    // ЛОГ
    // ================================================================

    private void AddLog(
        string message,
        Brush brush)
    {
        LogEntries.Add(
            new LogEntry(
                message,
                brush));

        if (LogEntries.Count > 1500)
        {
            LogEntries.RemoveAt(0);
        }
    }

    private void AddDeviceLog(
    string title)
    {
        AddLog(
            title,
            Brushes.LightBlue);

        foreach (
            string line in
            _dfuService.GetConnectedDevicesLog())
        {
            AddLog(
                line,
                Brushes.LightGray);
        }
    }

    // ================================================================
    // STATUS
    // ================================================================

    private void UpdateStatus(
        string message,
        Brush brush)
    {
        StatusText = message;
        StatusBrush = brush;
    }
}