using DroneFlasher.Helpers;
using DroneFlasher.Models;
using DroneFlasher.Services;
using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.IO.Ports;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace DroneFlasher.ViewModels;

public class MainViewModel : BaseViewModel
{
    private string? _deviceAwaitingDisconnect;
    private bool _isScanning;
    private bool _isBusy;
    private string[] _knownPorts = [];
    private string[] _lastSystemPorts = [];
    private string _dumpPath = string.Empty;
    private string _firmwarePath = string.Empty;
    private bool _isAutoFlashEnabled;
    private DateTime _lastActivityTime;
    private readonly DispatcherTimer _scanTimer;

    public ObservableCollection<LogEntry> LogEntries { get; } = new();

    public ICommand CopyLogCommand { get; }
    public ICommand TestDfuCommand { get; }
    public ICommand SelectDumpCommand { get; }
    public ICommand SelectFirmwareCommand { get; }
    public ICommand ManualFlashCommand { get; }
    public ICommand ClearLogCommand { get; }

    private readonly ConfigurationService _configService;
    private readonly DfuService _dfuService;
    private readonly DfuWatcherService _dfuWatcherService;
    private readonly FlightControllerWatcherService _fcWatcherService;

    public MainViewModel(
        ConfigurationService configService,
        DfuService dfuService,
        DfuWatcherService dfuWatcherService,
        FlightControllerWatcherService fcWatcherService)
    {
        _configService = configService;
        _dfuService = dfuService;
        _dfuWatcherService = dfuWatcherService;
        _fcWatcherService = fcWatcherService;

        CopyLogCommand = new RelayCommand(_ => CopyEntireLog());
        SelectDumpCommand = new RelayCommand(_ => SelectFile(p => DumpPath = p, "TXT files (*.txt)|*.txt"));
        SelectFirmwareCommand = new RelayCommand(_ => SelectFile(p => FirmwarePath = p, "HEX files (*.hex)|*.hex"));
        ManualFlashCommand = new RelayCommand(async _ => await ExecuteManualFlashAsync(), _ => !IsBusy);
        TestDfuCommand = new RelayCommand(async _ => await ExecuteTestDfuAsync(), _ => !IsBusy);
        ClearLogCommand = new RelayCommand(_ => LogEntries.Clear());

        try
        {
            _knownPorts = _fcWatcherService.GetFlightControllerPorts().ToArray();
            _lastSystemPorts = SerialPort.GetPortNames();
        }
        catch
        {
            _knownPorts = [];
            _lastSystemPorts = [];
        }

        _scanTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _scanTimer.Tick += ScanTimer_Tick;
    }

    public void StartMonitoring() => _scanTimer.Start();

    #region Bindable Properties
    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            if (SetField(ref _isBusy, value))
                CommandManager.InvalidateRequerySuggested();
        }
    }

    private string _statusText = "Очікування налаштувань...";
    public string StatusText
    {
        get => _statusText;
        set => SetField(ref _statusText, value);
    }

    private Brush _statusBrush = Brushes.Gray;
    public Brush StatusBrush
    {
        get => _statusBrush;
        set => SetField(ref _statusBrush, value);
    }

    public bool IsAutoFlashEnabled
    {
        get => _isAutoFlashEnabled;
        set
        {
            if (!SetField(ref _isAutoFlashEnabled, value)) return;

            if (value)
            {
                _deviceAwaitingDisconnect = null;
                _lastActivityTime = DateTime.UtcNow;

                bool hasFirmware = !string.IsNullOrWhiteSpace(FirmwarePath) && File.Exists(FirmwarePath);
                bool hasDump = !string.IsNullOrWhiteSpace(DumpPath) && File.Exists(DumpPath);

                if (!hasFirmware && !hasDump)
                {
                    UpdateStatus("Оберіть прошивку або конфігурацію!", Brushes.Orange);
                }
                else
                {
                    UpdateStatus("Пошук підключеного дрона...", Brushes.DodgerBlue);
                }
            }
            else
            {
                UpdateStatus("Автозавантаження вимкнено", Brushes.Gray);
            }
        }
    }

    public string DumpPath
    {
        get => _dumpPath;
        set
        {
            if (SetField(ref _dumpPath, value))
            {
                OnConfigurationFilesChanged();
            }
        }
    }

    public string FirmwarePath
    {
        get => _firmwarePath;
        set
        {
            if (SetField(ref _firmwarePath, value))
            {
                OnConfigurationFilesChanged();
            }
        }
    }

    private void OnConfigurationFilesChanged()
    {
        if (IsAutoFlashEnabled && !IsBusy)
        {
            bool hasFirmware = !string.IsNullOrWhiteSpace(FirmwarePath) && File.Exists(FirmwarePath);
            bool hasDump = !string.IsNullOrWhiteSpace(DumpPath) && File.Exists(DumpPath);

            if (hasFirmware || hasDump)
            {
                _lastActivityTime = DateTime.UtcNow;
                _deviceAwaitingDisconnect = null;
                UpdateStatus("Пошук підключеного дрона...", Brushes.DodgerBlue);

                _ = ProcessCurrentDeviceAsync();
            }
            else
            {
                UpdateStatus("Оберіть прошивку або конфігурацію!", Brushes.Orange);
            }
        }
    }

    #endregion

    #region Test DFU
    private async Task ExecuteTestDfuAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        LogEntries.Clear();

        try
        {
            UpdateStatus("Пошук COM-порту...", Brushes.Orange);
            var ports = await Task.Run(() => _fcWatcherService.GetFlightControllerPorts().ToArray());

            if (ports.Length == 0)
            {
                UpdateStatus("✖ COM-порт не знайдено", Brushes.IndianRed);
                AddLog("[TEST] Немає підключених COM-портів.", Brushes.IndianRed);
                return;
            }

            string targetPort = ports[0];
            AddLog($"[TEST] Знайдено порт: {targetPort}", Brushes.LightGreen);

            var statusProgress = new Progress<string>(msg => UpdateStatus(msg, Brushes.Orange));
            var logProgress = new Progress<LogEntry>(AddLogEntry);

            bool isReady = await _dfuService.EnterBootloaderAndWaitAsync(targetPort, statusProgress, logProgress);

            if (isReady)
                UpdateStatus("✔ Тест успішний: DFU готовий", Brushes.LimeGreen);
            else
                UpdateStatus("✖ Тест не пройдено", Brushes.IndianRed);
        }
        catch (Exception ex)
        {
            UpdateStatus($"Збій тесту: {ex.Message}", Brushes.IndianRed);
            AddLog($"[TEST] Помилка: {ex.Message}", Brushes.IndianRed);
        }
        finally
        {
            IsBusy = false;
        }
    }
    #endregion

    private readonly SemaphoreSlim _pipelineLock = new(1, 1);


    #region Manual Flashing
    private async Task ExecuteManualFlashAsync()
    {
        if (IsBusy) return;
        _scanTimer.Stop();

        try
        {
            var ports = await Task.Run(() => _fcWatcherService.GetFlightControllerPorts().ToArray());

            if (ports.Length > 0)
            {
                await ExecutePipelineAsync(ports[0]);
                return;
            }

            if (!string.IsNullOrWhiteSpace(FirmwarePath) && await _dfuWatcherService.IsDeviceInDfuModeAsync())
            {
                await ExecutePipelineAsync(portName: null);
                return;
            }

            UpdateStatus("Помилка: дрон не знайдено (перевірте підключення)!", Brushes.IndianRed);
        }
        finally
        {
            await ResetKnownPortsAsync();
            await Task.Delay(500);
            if (IsAutoFlashEnabled) _scanTimer.Start();
        }
    }
    #endregion

    #region Pipeline Execution
    private async Task ExecutePipelineAsync(string? portName)
    {
        if (!await _pipelineLock.WaitAsync(0))
            return;

        IsBusy = true;
        LogEntries.Clear();

        try
        {
            bool hasFirmware = !string.IsNullOrWhiteSpace(FirmwarePath) && File.Exists(FirmwarePath);
            bool hasDump = !string.IsNullOrWhiteSpace(DumpPath) && File.Exists(DumpPath);

            if (!hasFirmware && !hasDump)
            {
                UpdateStatus("Оберіть прошивку або конфігурацію!", Brushes.Orange);
                return;
            }

            var statusProgress = new Progress<string>(msg => UpdateStatus(msg, Brushes.Orange));
            var logProgress = new Progress<LogEntry>(AddLogEntry);

            // 1. Прошивка через DFU
            if (hasFirmware)
            {
                bool isDfuReady = await _dfuService.EnterBootloaderAndWaitAsync(portName, statusProgress, logProgress);

                if (!isDfuReady)
                    throw new InvalidOperationException("Помилка підготовки STM32 Bootloader.");

                UpdateStatus("Запис прошивки...", Brushes.DodgerBlue);
                await _dfuService.FlashFirmwareAsync(FirmwarePath, statusProgress, logProgress);
                AddLog("[DFU] ✓ Прошивку успішно записано.", Brushes.LimeGreen);

                if (hasDump)
                {
                    UpdateStatus("Очікування перезавантаження", Brushes.Orange);
                    AddLog("[PIPELINE] Очікування появи COM-порту після прошивки...", Brushes.LightBlue);

                    portName = await WaitForNewPortAsync(10);
                    if (string.IsNullOrEmpty(portName))
                        throw new InvalidOperationException("Плата не підключилася як COM-порт після прошивки!");

                    AddLog($"[PIPELINE] Польотник виявлено на порту: {portName}", Brushes.LimeGreen);
                    await Task.Delay(1000);
                }
            }

            // 2. Заливка налаштувань (Dump)
            if (hasDump)
            {
                if (string.IsNullOrEmpty(portName))
                {
                    var ports = await Task.Run(() => _fcWatcherService.GetFlightControllerPorts().ToArray());
                    portName = ports.FirstOrDefault();
                }

                if (string.IsNullOrEmpty(portName))
                    throw new InvalidOperationException("Не знайдено активний COM-порт польотника!");

                await _configService.ApplyDumpAsync(portName, DumpPath, statusProgress, logProgress);
            }

            if (string.IsNullOrEmpty(portName))
            {
                portName = await WaitForNewPortAsync(3);
            }

            string completionMessage = IsAutoFlashEnabled
            ? "ГОТОВО! ПІДКЛЮЧІТЬ НАСТУПНИЙ"
            : "ГОТОВО!";
            UpdateStatus(completionMessage, Brushes.LimeGreen);

            // Якщо був чистий DFU без повернення в COM — блокуємо по маркеру DFU.
            _deviceAwaitingDisconnect = IsAutoFlashEnabled
                        ? (portName ?? "DFU_DEVICE")
                        : null;
        }
        catch (Exception ex)
        {
            UpdateStatus($"ЗБІЙ: {ex.Message}", Brushes.IndianRed);
            AddLog($"[PIPELINE] ✖ {ex.Message}", Brushes.IndianRed);
            _deviceAwaitingDisconnect = null;
        }
        finally
        {
            IsBusy = false;
            _pipelineLock.Release();
        }
    }
    #endregion

    #region Device Polling & Monitoring
    private async void ScanTimer_Tick(object? sender, EventArgs e)
    {
        // Не скануємо, якщо операція вже йде або автозавантаження вимкнене
        if (_isScanning || IsBusy || !IsAutoFlashEnabled)
            return;

        // Перевірка наявності хоча б одного вибраного файлу
        bool hasFirmware = !string.IsNullOrWhiteSpace(FirmwarePath) && File.Exists(FirmwarePath);
        bool hasDump = !string.IsNullOrWhiteSpace(DumpPath) && File.Exists(DumpPath);
        if (!hasFirmware && !hasDump)
            return;

        _isScanning = true;

        try
        {
            // -----------------------------------------------------------------
            // ЕТАП 1: Якщо ми чекаємо, поки користувач висмикне прошитий борт
            // -----------------------------------------------------------------
            if (!string.IsNullOrEmpty(_deviceAwaitingDisconnect))
            {
                bool isStillPlugged;

                if (_deviceAwaitingDisconnect == "DFU_DEVICE")
                {
                    isStillPlugged = await _dfuWatcherService.IsDeviceInDfuModeAsync();
                }
                else
                {
                    var currentRawPorts = SerialPort.GetPortNames();
                    isStillPlugged = currentRawPorts.Contains(_deviceAwaitingDisconnect, StringComparer.OrdinalIgnoreCase);
                }

                if (isStillPlugged)
                {
                    // Дрон ще в роз'ємі — чекаємо далі
                    return;
                }

                // Дрон нарешті витягнули з роз'єму!
                _deviceAwaitingDisconnect = null;
                UpdateStatus("Готовий до підключення нового борту...", Brushes.DodgerBlue);
                AddLog("[AUTO] Борт від'єднано. Очікування наступного...", Brushes.Gray);
                return;
            }

            // -----------------------------------------------------------------
            // ЕТАП 2: Пошук нового борту для прошивки
            // -----------------------------------------------------------------

            // 2.1. Перевірка DFU (якщо підключили із затиснутою кнопкою BOOT)
            if (hasFirmware && await _dfuWatcherService.IsDeviceInDfuModeAsync())
            {
                _scanTimer.Stop();
                try
                {
                    AddLog("[AUTO] Виявлено борт у режимі DFU!", Brushes.LightGreen);
                    await ExecutePipelineAsync(portName: null);
                }
                finally
                {
                    if (IsAutoFlashEnabled) _scanTimer.Start();
                }
                return;
            }

            // 2.2. Перевірка COM-портів через WMI
            // Спочатку робимо швидку перевірку: чи взагалі є якісь COM-порти в системі?
            var rawPorts = SerialPort.GetPortNames();
            if (rawPorts.Length == 0)
                return;

            // Якщо порти є — запитуємо польотники через WMI
            var fcPorts = await Task.Run(() => _fcWatcherService.GetFlightControllerPorts());
            if (fcPorts.Count > 0)
            {
                string targetPort = fcPorts[0];

                _scanTimer.Stop();
                try
                {
                    AddLog($"[AUTO] Виявлено польотний контролер на {targetPort}!", Brushes.LightGreen);
                    await ExecutePipelineAsync(targetPort);
                }
                finally
                {
                    if (IsAutoFlashEnabled) _scanTimer.Start();
                }
            }
        }
        finally
        {
            _isScanning = false;
        }
    }
    #endregion

    #region Helpers
    private async Task ProcessCurrentDeviceAsync()
    {
        if (IsBusy || !IsAutoFlashEnabled) return;

        bool hasFirmware = !string.IsNullOrWhiteSpace(FirmwarePath) && File.Exists(FirmwarePath);
        bool hasDump = !string.IsNullOrWhiteSpace(DumpPath) && File.Exists(DumpPath);

        if (!hasFirmware && !hasDump)
        {
            UpdateStatus("Оберіть прошивку або конфігурацію.", Brushes.Orange);
            return;
        }

        // 1. Перевірка DFU (якщо дрон уже був запущений кнопкою Boot)
        if (hasFirmware && await _dfuWatcherService.IsDeviceInDfuModeAsync())
        {
            _scanTimer.Stop();
            try
            {
                UpdateStatus("Виявлено дрон у режимі STM32 Bootloader...", Brushes.DodgerBlue);
                AddLog("[AUTO] Підхоплено дрон у DFU режимі.", Brushes.LightBlue);

                await ExecutePipelineAsync(portName: null);
            }
            finally
            {
                _lastActivityTime = DateTime.UtcNow;
                if (IsAutoFlashEnabled) _scanTimer.Start();
            }
            return;
        }

        // 2. Перевірка звичайного COM-порту польотника
        var existingPorts = await Task.Run(() => _fcWatcherService.GetFlightControllerPorts().ToArray());
        if (existingPorts.Length > 0)
        {
            string targetPort = existingPorts[0];

            _scanTimer.Stop();
            try
            {
                UpdateStatus($"Виявлено підключений дрон на {targetPort}...", Brushes.DodgerBlue);
                AddLog($"[AUTO] Підхоплено вже підключений дрон: {targetPort}", Brushes.LightBlue);

                await ExecutePipelineAsync(targetPort);
            }
            finally
            {
                _lastActivityTime = DateTime.UtcNow;
                if (IsAutoFlashEnabled) _scanTimer.Start();
            }
        }
        else
        {
            UpdateStatus("Очікування підключення дрона...", Brushes.DodgerBlue);
        }
    }

    private async Task<string?> WaitForNewPortAsync(int timeoutSeconds, CancellationToken ct = default)
    {
        await Task.Delay(500, ct);
        var sw = Stopwatch.StartNew();
        var timeoutMs = timeoutSeconds * 1000;

        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            ct.ThrowIfCancellationRequested();

            string[] ports;
            try
            {
                ports = await Task.Run(() => _fcWatcherService.GetFlightControllerPorts().ToArray(), ct);
            }
            catch
            {
                ports = [];
            }

            if (ports.Length > 0)
                return ports.Last();

            await Task.Delay(350, ct);
        }

        return null;
    }

    private async Task ResetKnownPortsAsync()
    {
        try
        {
            _knownPorts = await Task.Run(() => _fcWatcherService.GetFlightControllerPorts().ToArray());
            _lastSystemPorts = SerialPort.GetPortNames();
        }
        catch
        {
            _knownPorts = [];
            _lastSystemPorts = [];
        }
    }

    private void CopyEntireLog()
    {
        string text = string.Join(Environment.NewLine, LogEntries.Select(entry => entry.Text));
        Clipboard.SetText(text);
    }

    private void SelectFile(Action<string> setPath, string filter)
    {
        var dlg = new OpenFileDialog { Filter = filter };
        if (dlg.ShowDialog() == true) setPath(dlg.FileName);
    }

    private void AddLog(string message, Brush brush) => AddLogEntry(new LogEntry(message, brush));

    private void AddLogEntry(LogEntry entry)
    {
        LogEntries.Add(entry);
        if (LogEntries.Count > 1500) LogEntries.RemoveAt(0);
    }

    private void UpdateStatus(string message, Brush brush)
    {
        StatusText = message;
        StatusBrush = brush;
    }
    #endregion
}