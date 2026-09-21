using DroneFlasher.Options;
using DroneFlasher.Services;
using DroneFlasher.ViewModels;
using DroneFlasher.Views;
using LibUsbDotNet.LibUsb;
using Microsoft.Extensions.DependencyInjection;
using System.Windows;

namespace DroneFlasher;

public partial class App : Application
{
    public static IServiceProvider ServiceProvider { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        LibUsbBootstrap.Initialize();

        base.OnStartup(e);

        var services = new ServiceCollection();

        services.AddSingleton<UsbContext>(); services.AddSingleton<FlightControllerOptions>();

        services.AddSingleton<ConfigurationService>();
        services.AddSingleton<DfuService>();
        services.AddSingleton<DfuWatcherService>();
        services.AddSingleton<FlightControllerWatcherService>();

        services.AddTransient<MainViewModel>();
        services.AddTransient<MainWindow>();

        ServiceProvider = services.BuildServiceProvider();

        var mainWindow = ServiceProvider.GetRequiredService<MainWindow>();
        mainWindow.Show();
        mainWindow.Show();

        mainWindow.Dispatcher.BeginInvoke(
            new Action(() =>
            {
                if (mainWindow.DataContext is MainViewModel vm)
                {
                    vm.StartMonitoring();
                }
            }),
            System.Windows.Threading.DispatcherPriority.ApplicationIdle);

    }
}