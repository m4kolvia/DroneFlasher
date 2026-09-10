using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;

namespace DroneFlasher.Services;

public static class DeviceWatcherService
{
    private static readonly string[] AllowedFlightControllerSignatures = new[]
    {
        "VID_0483&PID_5740", // STM32 VCP
        "VID_2E3C&PID_5740", // AT32 VCP
        "VID_28E9&PID_018A", // GD32 VCP
        "VID_314B&PID_5740", // APM32 VCP
        "VID_3997&PID_5740", // X32 VCP
        "VID_10C4&PID_EA60", // CP210x USB bridge
        "VID_1A86&PID_7523", // CH340 USB bridge
        "VID_2341"           // Arduino
    };

    public static List<string> GetFlightControllerPorts()
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

                bool isDrone = AllowedFlightControllerSignatures.Any(sig =>
                    pnpId.Contains(sig, StringComparison.OrdinalIgnoreCase));

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
        catch
        {
            // Повертаємо порожній список при тимчасовій зайнятості WMI
        }

        return matchedPorts;
    }
}