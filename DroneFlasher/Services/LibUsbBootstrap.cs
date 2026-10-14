
using System.IO;
using System.Reflection;

namespace DroneFlasher.Helpers
{
    public static class LibUsbBootstrap
    {
        public static void Initialize()
        {
            string dllName = "libusb-1.0.dll";
            string tempPath = Path.Combine(Path.GetTempPath(), dllName);

            var assembly = Assembly.GetExecutingAssembly();
            string resourceName = "DroneFlasher.Native.x64.libusb_1_0.dll";

            using (Stream? stream = assembly.GetManifestResourceStream(resourceName))
            {
                if (stream == null) return;

                using (FileStream fileStream = new FileStream(
                    tempPath, FileMode.Create, FileAccess.Write))
                {
                    stream.CopyTo(fileStream);
                }
            }
        }
    }
}
