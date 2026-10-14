using System.IO;
using System.Windows;
using System.Windows.Threading;
using DroneFlasher.Helpers;
using DroneFlasher.Models;
using DroneFlasher.Services;
using DroneFlasher.ViewModels;
using DroneFlasher.Views;
using LibUsbDotNet.LibUsb;
using Microsoft.Extensions.DependencyInjection;

namespace DroneFlasher;

public partial class App : Application
{
    public static IServiceProvider ServiceProvider { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        this.DispatcherUnhandledException += App_DispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;

        LibUsbBootstrap.Initialize();

        base.OnStartup(e);

        var services = new ServiceCollection();

        services.AddSingleton<UsbContext>();
        services.AddSingleton<FlightControllerUsbSignatures>();

        services.AddSingleton<ConfigurationService>();
        services.AddSingleton<DfuService>();
        services.AddSingleton<DfuWatcherService>();
        services.AddSingleton<FlightControllerWatcherService>();

        services.AddTransient<MainViewModel>();
        services.AddTransient<MainWindow>();

        ServiceProvider = services.BuildServiceProvider();

        var mainWindow = ServiceProvider.GetRequiredService<MainWindow>();
        mainWindow.Show();
    }

    private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        LogCrash(e.Exception, "UI Thread");
        e.Handled = true; 
    }

    private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        LogCrash(e.ExceptionObject as Exception, "AppDomain");
    }

    private void LogCrash(Exception? ex, string source)
    {
        if (ex == null) return;

        string logFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash_log.txt");

        string errorText = $"[{DateTime.Now}] КРИТИЧНИЙ ЗБІЙ ({source}):\n" +
                           $"Помилка: {ex.Message}\n\n" +
                           $"Стек викликів:\n{ex.StackTrace}\n";

        if (ex.InnerException != null)
        {
            errorText += $"\nВнутрішня помилка:\n{ex.InnerException.Message}\n{ex.InnerException.StackTrace}\n";
        }

        try
        {
            File.AppendAllText(logFile, errorText + new string('-', 40) + "\n");

            MessageBox.Show(errorText, "Фатальна помилка програми", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch
        {
        }
    }
}