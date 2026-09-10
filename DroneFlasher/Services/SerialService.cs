using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using DroneFlasher.Models;

namespace DroneFlasher.Services;

public class SerialService
{
    private const int BaudRate = 115200;
    private const int CommandResponseTimeoutMs = 500;
    private const int ResponseIdleTimeoutMs = 30;
    private const int CommandDelayMs = 5;

    public async Task ApplyDumpAsync(
        string portName,
        string dumpFilePath,
        IProgress<string> statusProgress,
        IProgress<LogEntry> logProgress,
        CancellationToken ct = default)
    {
        if (!File.Exists(dumpFilePath))
        {
            throw new FileNotFoundException(
                "Файл конфігурації не знайдено.",
                dumpFilePath);
        }

        string[] lines =
            await File.ReadAllLinesAsync(
                dumpFilePath,
                ct);

        await ApplyConfigurationAsync(
            portName,
            lines,
            statusProgress,
            logProgress,
            ct);
    }

    public async Task<string?> FindFlightControllerPortAsync(
        CancellationToken ct = default)
    {
        string[] ports;

        try
        {
            ports = SerialPort.GetPortNames();
        }
        catch
        {
            return null;
        }

        foreach (string portName in ports)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                using var port =
                    new SerialPort(
                        portName,
                        BaudRate)
                    {
                        ReadTimeout = 300,
                        WriteTimeout = 300,
                        DtrEnable = true,
                        RtsEnable = true,
                        NewLine = "\r\n"
                    };

                port.Open();

                try
                {
                    port.DiscardInBuffer();
                }
                catch
                {
                }

                port.Write("#\r\n");

                await Task.Delay(
                    150,
                    ct);

                string response =
                    port.ReadExisting();

                if (!string.IsNullOrWhiteSpace(response))
                {
                    return portName;
                }
            }
            catch
            {
            }
        }

        return null;
    }

    public async Task ApplyConfigurationAsync(
        string portName,
        string[] configLines,
        IProgress<string> statusProgress,
        IProgress<LogEntry> logProgress,
        CancellationToken ct = default)
    {
        using var port =
            new SerialPort(
                portName,
                BaudRate)
            {
                ReadTimeout = 400,
                WriteTimeout = 400,
                DtrEnable = true,
                RtsEnable = true,
                NewLine = "\r\n"
            };

        statusProgress.Report(
            $"Підключення до {portName}...");

        port.Open();

        try
        {
            port.Write("#\r\n");

            await Task.Delay(
                250,
                ct);

            try
            {
                port.DiscardInBuffer();
            }
            catch
            {
            }

            logProgress.Report(
                new LogEntry(
                    $"Порт {portName} готовий.",
                    Brushes.Gold));
        }
        catch (Exception ex)
        {
            logProgress.Report(
                new LogEntry(
                    $"Помилка підключення: {ex.Message}",
                    Brushes.IndianRed));
        }

        var skippedCommands =
            new HashSet<string>(
                StringComparer.OrdinalIgnoreCase)
            {
                "save",
                "exit",
                "reboot",
                "bl",
                "bootloader",
                "batch end"
            };

        var commands =
            configLines
                .Select(line => line.Trim())
                .Where(line =>
                    !string.IsNullOrWhiteSpace(line))
                .Where(line =>
                    !line.StartsWith("#"))
                .Where(line =>
                    !skippedCommands.Contains(line))
                .Where(line =>
                    !line.Equals(
                        "defaults",
                        StringComparison.OrdinalIgnoreCase))
                .Where(line =>
                    !line.StartsWith(
                        "defaults ",
                        StringComparison.OrdinalIgnoreCase))
                .ToList();

        if (commands.Count == 0)
        {
            logProgress.Report(
                new LogEntry(
                    "У файлі немає команд.",
                    Brushes.Gold));

            return;
        }

        int totalCommands =
            commands.Count;

        int processedCommands = 0;

        statusProgress.Report(
            $"Заливка конфігурації ({totalCommands} команд)...");

        logProgress.Report(
            new LogEntry(
                $"Виконання {totalCommands} команд...",
                Brushes.Gold));

        foreach (string line in commands)
        {
            ct.ThrowIfCancellationRequested();

            processedCommands++;

            try
            {
                port.Write(line + "\r\n");

                string response =
                    await ReadResponseAsync(
                        port,
                        ct);

                if (string.IsNullOrWhiteSpace(response))
                {
                    logProgress.Report(
                        new LogEntry(
                            line,
                            Brushes.Gold));
                }
                else if (ContainsCliError(response))
                {
                    logProgress.Report(
                        new LogEntry(
                            line,
                            Brushes.IndianRed));
                }
                else
                {
                    logProgress.Report(
                        new LogEntry(
                            line,
                            Brushes.LightGreen));
                }
            }
            catch
            {
                logProgress.Report(
                    new LogEntry(
                        line,
                        Brushes.IndianRed));
            }

            await Task.Delay(
                CommandDelayMs,
                ct);

            UpdateProgress(
                statusProgress,
                processedCommands,
                totalCommands);
        }

        statusProgress.Report(
            "Завершення пакету команд...");

        logProgress.Report(
            new LogEntry(
                "Завершення пакету...",
                Brushes.Gold));

        try
        {
            port.Write("batch end\r\n");

            await ReadResponseAsync(
                port,
                ct);
        }
        catch
        {
        }

        statusProgress.Report(
            "Збереження параметрів...");

        logProgress.Report(
            new LogEntry(
                "Збереження...",
                Brushes.Gold));

        try
        {
            port.Write("save\r\n");

            await Task.Delay(
                1500,
                ct);
        }
        catch
        {
        }

        logProgress.Report(
            new LogEntry(
                "Завантаження конфігурації завершено.",
                Brushes.LightGreen));

        statusProgress.Report(
            "Конфігурацію успішно застосовано!");
    }

    private static async Task<string> ReadResponseAsync(
        SerialPort port,
        CancellationToken ct)
    {
        string response =
            string.Empty;

        var totalStopwatch =
            System.Diagnostics.Stopwatch.StartNew();

        var idleStopwatch =
            System.Diagnostics.Stopwatch.StartNew();

        bool receivedAnything = false;

        while (
            totalStopwatch.ElapsedMilliseconds <
            CommandResponseTimeoutMs)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                if (port.BytesToRead > 0)
                {
                    string chunk =
                        port.ReadExisting();

                    if (!string.IsNullOrEmpty(chunk))
                    {
                        response += chunk;

                        receivedAnything = true;

                        idleStopwatch.Restart();
                    }
                }
            }
            catch
            {
                break;
            }

            if (receivedAnything &&
                idleStopwatch.ElapsedMilliseconds >=
                ResponseIdleTimeoutMs)
            {
                break;
            }

            await Task.Delay(
                4,
                ct);
        }

        return response;
    }

    private static bool ContainsCliError(
        string response)
    {
        if (string.IsNullOrWhiteSpace(response))
            return false;

        return
            response.Contains(
                "error",
                StringComparison.OrdinalIgnoreCase)
            ||
            response.Contains(
                "###",
                StringComparison.OrdinalIgnoreCase)
            ||
            response.Contains(
                "unknown",
                StringComparison.OrdinalIgnoreCase)
            ||
            response.Contains(
                "invalid",
                StringComparison.OrdinalIgnoreCase)
            ||
            response.Contains(
                "failed",
                StringComparison.OrdinalIgnoreCase)
            ||
            response.Contains(
                "not found",
                StringComparison.OrdinalIgnoreCase);
    }

    private static void UpdateProgress(
        IProgress<string> statusProgress,
        int current,
        int total)
    {
        if (total <= 0)
            return;

        int percent =
            current * 100 / total;

        statusProgress.Report(
            $"Запис параметрів: {current}/{total} ({percent}%)");
    }
}