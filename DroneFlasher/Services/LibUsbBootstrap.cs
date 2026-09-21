using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace DroneFlasher.Services;

public static class LibUsbBootstrap
{
    private static string? _dllPath;

    public static void Initialize()
    {
        if (_dllPath != null)
            return;

        _dllPath = ExtractLibUsb();

        NativeLibrary.Load(_dllPath);
    }

    private static string ExtractLibUsb()
    {
        string tempDirectory = Path.Combine(Path.GetTempPath(), "DroneFlasher", "libusb");
        string dllPath = Path.Combine(tempDirectory, "libusb-1.0.dll");

        Directory.CreateDirectory(tempDirectory);
        Assembly assembly = typeof(LibUsbBootstrap).Assembly;

        string? resourceName =
            assembly
                .GetManifestResourceNames()
                .FirstOrDefault(name =>
                    name.EndsWith(
                        "libusb-1.0.dll",
                        StringComparison.OrdinalIgnoreCase));

        if (resourceName == null)
        {
            throw new FileNotFoundException("Вбудований libusb-1.0.dll не знайдено.");
        }

        using Stream? resourceStream = assembly.GetManifestResourceStream(resourceName);

        if (resourceStream == null)
        {
            throw new FileNotFoundException("Не вдалося прочитати вбудований libusb-1.0.dll.");
        }

        using FileStream fileStream = new FileStream(dllPath, FileMode.Create, FileAccess.Write, FileShare.Read);

        resourceStream.CopyTo(fileStream);

        return dllPath;
    }
}