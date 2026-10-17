using static System.StringComparison;

namespace DroneFlasher.Services.Configuration;
public static class CliResponseParser
{
    public static bool ContainsError(string response)
    {
        if (string.IsNullOrWhiteSpace(response)) return false;

        return response.Contains("error", OrdinalIgnoreCase)
            || response.Contains("###", OrdinalIgnoreCase)
            || response.Contains("unknown", OrdinalIgnoreCase)
            || response.Contains("invalid", OrdinalIgnoreCase)
            || response.Contains("failed", OrdinalIgnoreCase)
            || response.Contains("parse error", OrdinalIgnoreCase);
    }

    public static string ExtractCleanError(string response, string originalCommand)
    {
        var lines = response.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim())
            .Where(l => !string.IsNullOrEmpty(l))
            .Where(l => !l.Equals(originalCommand, StringComparison.OrdinalIgnoreCase) && l != "#")
            .ToList();

        return lines.Count > 0 ? string.Join(" | ", lines) : "ПОМИЛКА CLI";
    }
}