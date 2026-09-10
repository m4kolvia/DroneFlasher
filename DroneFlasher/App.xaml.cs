using DroneFlasher.Services;
using System.Windows;

namespace DroneFlasher;

public partial class App : Application
{
    protected override void OnStartup(
        StartupEventArgs e)
    {
        LibUsbBootstrap.Initialize();

        base.OnStartup(e);
    }
}