using DroneFlasher.Helpers;
using DroneFlasher.Models;
using DroneFlasher.Services.Configuration;
using DroneFlasher.Services.Firmware;
using DroneFlasher.Services.Usb;
using LibUsbDotNet.LibUsb;
using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.IO.Ports;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace DroneFlasher.ViewModels;

public class MainViewModel : BaseViewModel
{
    private string? _deviceAwaitingDisconnect;
    private bool _isScanning;
    private bool _isBusy;
    private readonly DispatcherTimer _scanTimer;
    private readonly SemaphoreSlim _pipelineLock = new(1, 1);
    public ObservableCollection<LogEntry> LogEntries { get; } = new();

    public ICommand CopyLogCommand { get; }
    public ICommand TestDfuCommand { get; }
    public ICommand SelectDumpCommand { get; }
    public ICommand SelectFirmwareCommand { get; }
    public ICommand ManualFlashCommand { get; }
    public ICommand ClearLogCommand { get; }
    public ICommand ClearFirmwareCommand { get; }
    public ICommand ClearDumpCommand { get; }
    public ICommand ToggleAutoFlashCommand { get; }

    private readonly ConfigurationService _configService;
    private readonly DfuService _dfuService;
    private readonly DfuWatcherService _dfuWatcherService;
    private readonly FlightControllerWatcherService _fcWatcherService;

    public MainViewModel(ConfigurationService configService, DfuService dfuService, DfuWatcherService dfuWatcherService, FlightControllerWatcherService fcWatcherService)
    {
        _configService = configService;
        _dfuService = dfuService;
        _dfuWatcherService = dfuWatcherService;
        _fcWatcherService = fcWatcherService;

        _scanTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _scanTimer.Tick += ScanTimer_Tick;
        CopyLogCommand = new RelayCommand(_ => CopyEntireLog());
        SelectDumpCommand = new RelayCommand(_ => SelectFile(p => DumpPath = p, "TXT files (*.txt)|*.txt"));
        SelectFirmwareCommand = new RelayCommand(_ => SelectFile(p => FirmwarePath = p, "HEX files (*.hex)|*.hex"));
        ManualFlashCommand = new RelayCommand(async _ => await ExecuteManualFlashAsync(), _ => !IsBusy);
        TestDfuCommand = new RelayCommand(async _ => await ExecuteTestDfuAsync(), _ => !IsBusy);
        ClearLogCommand = new RelayCommand(_ => LogEntries.Clear());
        ClearFirmwareCommand = new RelayCommand(_ => FirmwarePath = string.Empty, _ => !string.IsNullOrEmpty(FirmwarePath));
        ClearDumpCommand = new RelayCommand(_ => DumpPath = string.Empty, _ => !string.IsNullOrEmpty(DumpPath));
        ToggleAutoFlashCommand = new RelayCommand(
        _ => IsAutoFlashEnabled = !IsAutoFlashEnabled);
    }

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

    private bool _isAutoFlashEnabled;
    public bool IsAutoFlashEnabled
    {
        get => _isAutoFlashEnabled;
        set
        {
            if (!SetField(ref _isAutoFlashEnabled, value)) return;

            if (value)
            {
                _deviceAwaitingDisconnect = null;
                ValidateAndSetStatus();
                _scanTimer.Start();
            }
            else
            {
                _scanTimer.Stop();
                _deviceAwaitingDisconnect = null;

                ValidateAndSetStatus();
            }
        }
    }

    private string _firmwarePath = string.Empty;
    public string FirmwarePath
    {
        get => _firmwarePath;
        set
        {
            string sanitized = value?.Trim().Trim('"') ?? string.Empty;

            if (SetField(ref _firmwarePath, sanitized))
            {
                OnConfigurationFilesChanged();
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    private string _dumpPath = string.Empty;
    public string DumpPath
    {
        get => _dumpPath;
        set
        {
            string sanitized = value?.Trim().Trim('"') ?? string.Empty;

            if (SetField(ref _dumpPath, sanitized))
            {
                OnConfigurationFilesChanged();
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    private void OnConfigurationFilesChanged()
    {
        if (IsAutoFlashEnabled && !IsBusy && string.IsNullOrEmpty(_deviceAwaitingDisconnect))
        {
            ValidateAndSetStatus();
        }
    }
    #endregion

    private async Task ExecutePipelineAsync(string? portName)
    {
        if (!await _pipelineLock.WaitAsync(0)) return;

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

            ValidateAndSetStatus();

            IProgress<string> statusProgress = new Progress<string>(msg => UpdateStatus(msg, Brushes.Orange));
            var logProgress = new Progress<LogEntry>(AddLogEntry);

            if (hasFirmware)
            {
                bool isDfuReady = await _dfuService.EnterBootloaderAndWaitAsync(portName, statusProgress, logProgress);
                if (!isDfuReady) throw new InvalidOperationException("Помилка підготовки STM32 Bootloader.");

                UpdateStatus("Запис прошивки...", Brushes.DodgerBlue);
                bool firmwareWritten = await _dfuService.FlashFirmwareAsync(FirmwarePath, statusProgress, logProgress);

                if (!firmwareWritten)
                {
                    UpdateStatus("DFU перевірено. Прошивку не записано; конфігурацію не застосовано.", Brushes.Gold);
                    AddLog("[DFU] Тест завершено. Запис прошивки пропущено.", Brushes.Gold);

                    return;
                }

                if (hasDump)
                {
                    UpdateStatus("Очікування перезавантаження", Brushes.Orange);
                    portName = await WaitForNewPortAsync(10);
                    if (string.IsNullOrEmpty(portName))
                        throw new InvalidOperationException("Плата не підключилася як COM-порт після прошивки!");

                    await Task.Delay(1000);
                }
            }

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

                AddLog("[CONFIG] Перезавантаження після налаштувань...", Brushes.LimeGreen);
                UpdateStatus("Перезавантаження після налаштувань...", Brushes.Orange);

                var sw = Stopwatch.StartNew();
                while (sw.ElapsedMilliseconds < 2000)
                {
                    if (!SerialPort.GetPortNames().Contains(portName, StringComparer.OrdinalIgnoreCase))
                        break;

                    await Task.Delay(50);
                }

                var reconnectedPort = await WaitForNewPortAsync(10);
                if (!string.IsNullOrEmpty(reconnectedPort))
                {
                    portName = reconnectedPort;
                }
            }

            if (string.IsNullOrEmpty(portName))
            {
                portName = await WaitForNewPortAsync(3);
            }

            if (IsAutoFlashEnabled)
            {
                UpdateStatus("ГОТОВО! ВІД'ЄДНАЙТЕ ДРОН", Brushes.LimeGreen);
                _deviceAwaitingDisconnect = portName ?? "DFU_DEVICE";
            }
            else
            {
                if (hasFirmware && hasDump)
                    UpdateStatus("ГОТОВО! ПРОШИВКУ ТА КОНФІГУРАЦІЮ ЗАСТОСОВАНО", Brushes.LimeGreen);
                else if (hasFirmware)
                    UpdateStatus("ГОТОВО! ПРОШИВКУ ЗАПИСАНО", Brushes.LimeGreen);
                else if (hasDump)
                    UpdateStatus("ГОТОВО! КОНФІГУРАЦІЮ ЗАСТОСОВАНО", Brushes.LimeGreen);

                _deviceAwaitingDisconnect = null;
            }
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

    private async void ScanTimer_Tick(object? sender, EventArgs e)
    {
        if (_isScanning || IsBusy || !IsAutoFlashEnabled) return;

        _isScanning = true;

        try
        {
            bool hasFirmware = !string.IsNullOrWhiteSpace(FirmwarePath) && File.Exists(FirmwarePath);
            bool hasDump = !string.IsNullOrWhiteSpace(DumpPath) && File.Exists(DumpPath);

            if (!string.IsNullOrEmpty(_deviceAwaitingDisconnect))
            {
                bool isStillPlugged = await Task.Run(() => IsDronePhysicallyConnected());

                if (isStillPlugged)
                {
                    return;
                }

                _deviceAwaitingDisconnect = null;
                AddLog("[AUTO] Попередній борт фізично від'єднано.", Brushes.Gray);

                if (hasFirmware || hasDump)
                {
                    UpdateStatus("Автозапуск: очікування дрона...", Brushes.DodgerBlue);
                }
                else
                {
                    UpdateStatus("Оберіть прошивку або конфігурацію!", Brushes.Orange);
                }

                return;
            }

            if (!hasFirmware && !hasDump) return;

            if (hasFirmware)
            {
                bool isDfu = false;
                try { isDfu = await _dfuWatcherService.IsDeviceInDfuModeAsync(); } catch { }

                if (isDfu)
                {
                    AddLog("[AUTO] Виявлено борт у режимі DFU!", Brushes.LightGreen);
                    await ExecutePipelineAsync(portName: null);
                    return;
                }
            }

            string[] ports = [];
            try { ports = await Task.Run(() => _fcWatcherService.GetFlightControllerPorts().ToArray()); } catch { }

            if (ports.Length > 0)
            {
                string targetPort = ports[0];
                AddLog($"[AUTO] Виявлено польотник на {targetPort}!", Brushes.LightGreen);
                await ExecutePipelineAsync(targetPort);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Timer error: {ex.Message}");
        }
        finally
        {
            _isScanning = false;
        }
    }
    
    private async Task ExecuteManualFlashAsync()
    {
        if (IsBusy) return;

        if (IsAutoFlashEnabled) _scanTimer.Stop();

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
            if (IsAutoFlashEnabled) _scanTimer.Start();
        }
    }

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
                return;
            }

            string targetPort = ports[0];
            var statusProgress = new Progress<string>(msg => UpdateStatus(msg, Brushes.Orange));
            var logProgress = new Progress<LogEntry>(AddLogEntry);

            bool isReady = await _dfuService.EnterBootloaderAndWaitAsync(targetPort, statusProgress, logProgress);
            UpdateStatus(isReady ? "✔ Тест успішний" : "✖ Тест не пройдено", isReady ? Brushes.LimeGreen : Brushes.IndianRed);
        }
        catch (Exception ex)
        {
            UpdateStatus($"Збій тесту: {ex.Message}", Brushes.IndianRed);
        }
        finally
        {
            IsBusy = false;
        }
    }

    #region Helpers
    private async Task<string?> WaitForNewPortAsync(int timeoutSeconds, CancellationToken ct = default)
    {
        await Task.Delay(500, ct);
        var sw = Stopwatch.StartNew();
        var timeoutMs = timeoutSeconds * 1000;

        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var ports = await Task.Run(() => _fcWatcherService.GetFlightControllerPorts().ToArray(), ct);
                if (ports.Length > 0) return ports.Last();
            }
            catch { }

            await Task.Delay(350, ct);
        }

        return null;
    }

    private void CopyEntireLog() => Clipboard.SetText(string.Join(Environment.NewLine, LogEntries.Select(e => e.Text)));
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

    private bool IsDronePhysicallyConnected()
    {
        try
        {
            using var context = new UsbContext();
            using var deviceList = context.List();

            return deviceList.Any(d =>
                d.VendorId == 0x0483 || // STM32
                d.VendorId == 0x2E3C || // Artery AT32
                d.VendorId == 0x1A86 || // CH340
                d.VendorId == 0x10C4 || // CP2102
                d.VendorId == 0x0403    // FTDI
            );
        }
        catch
        {
            return false;
        }
    }

    private void ValidateAndSetStatus()
    {
        bool hasFirmwareText = !string.IsNullOrWhiteSpace(FirmwarePath);
        bool hasDumpText = !string.IsNullOrWhiteSpace(DumpPath);

        bool firmwareExists = hasFirmwareText && File.Exists(FirmwarePath);
        bool dumpExists = hasDumpText && File.Exists(DumpPath);

        if (firmwareExists || dumpExists)
        {
            UpdateStatus("Автозапуск: очікування дрона...", Brushes.DodgerBlue);
            return;
        }

        if (hasFirmwareText || hasDumpText)
        {
            UpdateStatus("Помилка: файл не знайдено!", Brushes.IndianRed);
            return;
        }

        UpdateStatus("Оберіть прошивку або конфігурацію!", Brushes.Orange);
    }
    #endregion
}