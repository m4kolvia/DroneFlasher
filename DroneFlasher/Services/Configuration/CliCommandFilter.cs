namespace DroneFlasher.Services.Configuration;

public static class CliCommandFilter
{
    private static readonly HashSet<string> SkippedCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "save", "exit", "reboot", "bl", "bootloader", "batch start", "batch end"
    };

    public static List<string> GetValidCommands(string[] rawLines)
    {
        return rawLines
            .Select(line => line.Trim())
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Where(line => !line.StartsWith("#"))
            .Where(line => !SkippedCommands.Contains(line))
            .Where(line => !line.Equals("defaults", StringComparison.OrdinalIgnoreCase))
            .Where(line => !line.StartsWith("defaults ", StringComparison.OrdinalIgnoreCase))
            .ToList();
    }
}