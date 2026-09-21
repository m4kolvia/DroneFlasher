using System.Diagnostics;
using System.IO;
using System.IO.Ports;
using System.Text;
using System.Windows.Media;
using DroneFlasher.Models;
using static System.StringComparison;

namespace DroneFlasher.Services;

public class ConfigurationService
{
    private const int BaudRate = 115200;
    private const int CommandTimeoutMs = 60;

    public async Task ApplyDumpAsync(string portName, string dumpFilePath, IProgress<string> statusProgress, IProgress<LogEntry> logProgress, CancellationToken ct = default)
    {
        if (!File.Exists(dumpFilePath))
            throw new FileNotFoundException("Файл конфігурації не знайдено.", dumpFilePath);

        string[] lines = await File.ReadAllLinesAsync(dumpFilePath, ct);
        await ApplyConfigurationAsync(portName, lines, statusProgress, logProgress, ct);
    }

    public async Task ApplyConfigurationAsync(string portName, string[] configLines, IProgress<string> statusProgress, IProgress<LogEntry> logProgress, CancellationToken ct = default)
    {
        using var port = new SerialPort(portName, BaudRate)
        {
            ReadTimeout = 100,
            WriteTimeout = 500,
            DtrEnable = true,
            RtsEnable = true,
            NewLine = "\r\n"
        };

        statusProgress.Report($"Підключення...");
        port.Open();

        try
        {
            port.DiscardInBuffer();
            port.Write("#\r\n");
            await Task.Delay(150, ct);
            port.DiscardInBuffer();
            logProgress.Report(new LogEntry($"Порт {portName} готовий до передачі.", Brushes.Gold));
        }
        catch (Exception ex)
        {
            logProgress.Report(new LogEntry($"Помилка підключення: {ex.Message}", Brushes.IndianRed));
        }

        var skippedCommands = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "save", "exit", "reboot", "bl", "bootloader", "batch start", "batch end"
        };

        var commands = configLines
            .Select(line => line.Trim())
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Where(line => !line.StartsWith("#"))
            .Where(line => !skippedCommands.Contains(line))
            .Where(line => !line.Equals("defaults", OrdinalIgnoreCase))
            .Where(line => !line.StartsWith("defaults ", OrdinalIgnoreCase))
            .ToList();

        if (commands.Count == 0)
        {
            logProgress.Report(new LogEntry("У файлі немає команд для виконання.", Brushes.Gold));
            return;
        }

        int totalCommands = commands.Count;
        logProgress.Report(new LogEntry($"Початок відправки {totalCommands} команд...", Brushes.DodgerBlue));

        try { port.Write("batch start\r\n"); } catch { }
        await Task.Delay(40, ct);
        port.DiscardInBuffer();

        int processedCommands = 0;
        var progressThrottle = Stopwatch.StartNew();

        foreach (string line in commands)
        {
            ct.ThrowIfCancellationRequested();
            processedCommands++;

            try
            {
                port.Write(line + "\r\n");

                string response = await WaitForPromptAsync(port, CommandTimeoutMs, ct);

                if (ContainsCliError(response))
                {
                    string errorDetail = ExtractCleanError(response, line);
                    logProgress.Report(new LogEntry($"✖ {line} -> {errorDetail}", Brushes.IndianRed));
                }
                else
                {
                    logProgress.Report(new LogEntry($"✓ {line}", Brushes.LightGreen));
                }
            }
            catch (Exception ex)
            {
                logProgress.Report(new LogEntry($"✖ {line} -> Збій запису: {ex.Message}", Brushes.IndianRed));
            }

            if (progressThrottle.ElapsedMilliseconds > 100 || processedCommands == totalCommands)
            {
                UpdateProgress(statusProgress, processedCommands, totalCommands);
                progressThrottle.Restart();
            }
        }

        logProgress.Report(new LogEntry("Завершення batch-режиму...", Brushes.Gold));

        try
        {
            port.Write("batch end\r\n");
            await Task.Delay(100, ct);
            port.DiscardInBuffer();
        }
        catch { }

        logProgress.Report(new LogEntry("Збереження налаштувань (save)...", Brushes.Gold));

        try
        {
            port.Write("save\r\n");
            await Task.Delay(1500, ct);
        }
        catch { }
    }

    private static async Task<string> WaitForPromptAsync(SerialPort port, int timeoutMs, CancellationToken ct)
    {
        var sb = new StringBuilder();
        var sw = Stopwatch.StartNew();

        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                if (port.BytesToRead > 0)
                {
                    string chunk = port.ReadExisting();
                    sb.Append(chunk);

                    if (chunk.Contains('#'))
                    {
                        break;
                    }
                }
            }
            catch
            {
                break;
            }

            await Task.Delay(2, ct);
        }

        return sb.ToString();
    }

    private static bool ContainsCliError(string response)
    {
        if (string.IsNullOrWhiteSpace(response))
            return false;

        return response.Contains("error", OrdinalIgnoreCase)
            || response.Contains("###", OrdinalIgnoreCase)
            || response.Contains("unknown", OrdinalIgnoreCase)
            || response.Contains("invalid", OrdinalIgnoreCase)
            || response.Contains("failed", OrdinalIgnoreCase)
            || response.Contains("parse error", OrdinalIgnoreCase);
    }

    private static string ExtractCleanError(string response, string originalCommand)
    {
        var lines = response.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim())
            .Where(l => !l.Equals(originalCommand, OrdinalIgnoreCase) && !l.StartsWith('#'))
            .ToList();

        return lines.Count > 0 ? string.Join(" | ", lines) : "ПОМИЛКА CLI";
    }

    private static void UpdateProgress(IProgress<string> statusProgress, int current, int total)
    {
        if (total <= 0) return;
        int percent = current * 100 / total;
        statusProgress.Report($"{percent}% ({current}/{total}");
    }
}