using DroneFlasher.Models;
using System.Management;

namespace DroneFlasher.Services;

public class FlightControllerWatcherService
{
    private readonly string[] _signatures;

    public FlightControllerWatcherService(FlightControllerUsbSignatures options)
    {
        _signatures = options.AllowedSignatures;
    }

    public List<string> GetFlightControllerPorts()
    {
        var matchedPorts = new List<string>();

        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT PNPDeviceID, Caption FROM Win32_PnPEntity WHERE ClassGuid=\"{4d36e978-e325-11ce-bfc1-08002be10318}\""
            );

            foreach (var device in searcher.Get())
            {
                string pnpId = device["PNPDeviceID"]?.ToString() ?? string.Empty;
                string caption = device["Caption"]?.ToString() ?? string.Empty;

                bool isDrone = _signatures.Any(sig => pnpId.Contains(sig, StringComparison.OrdinalIgnoreCase));

                if (isDrone)
                {
                    int startIdx = caption.LastIndexOf("(COM", StringComparison.OrdinalIgnoreCase);
                    if (startIdx != -1)
                    {
                        int endIdx = caption.IndexOf(')', startIdx);
                        if (endIdx != -1)
                        {
                            string portName = caption.Substring(startIdx + 1, endIdx - startIdx - 1).Trim();
                            matchedPorts.Add(portName);
                        }
                    }
                }
            }
        }
        catch { }

        return matchedPorts;
    }
}