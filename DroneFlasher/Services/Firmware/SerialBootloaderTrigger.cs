using System.Diagnostics;
using System.IO.Ports;

namespace DroneFlasher.Services.Firmware;

public static class SerialBootloaderTrigger
{
    public static async Task RebootToBootloaderAsync(string portName)
    {
        try
        {
            using var port = new SerialPort(portName, 115200)
            {
                ReadTimeout = 1000,
                WriteTimeout = 1000,
                DtrEnable = true,
                RtsEnable = true,
                NewLine = "\r\n"
            };

            port.Open();
            await Task.Delay(150);

            port.DiscardInBuffer();
            port.DiscardOutBuffer();

            port.Write("#");
            await Task.Delay(150);

            port.Write("bl\r\n");
            await Task.Delay(700);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[DFU] RebootToBootloaderAsync: {ex.Message}");
        }
    }
}