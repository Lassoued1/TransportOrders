using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using TransportOrders.App.Services;
using TransportOrders.App.ViewModels;
using TransportOrders.Data;

namespace TransportOrders.App;

public partial class App : Application
{
    private ServiceProvider? _services;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnDispatcherUnhandledException;

        var services = new ServiceCollection();
        services.AddTransportOrdersData(DatabasePaths.GetDefaultDatabasePath());
        services.AddSingleton<IUserMessageService, MessageBoxService>();
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();
        _services = services.BuildServiceProvider();

        var window = _services.GetRequiredService<MainWindow>();
        MainWindow = window;
        window.Show();

        try
        {
            await _services.GetRequiredService<DatabaseInitializer>().InitializeAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                window,
                $"The database could not be initialized and the application will close.\n\n{ex.GetBaseException().Message}",
                "Transport Orders",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        await _services.GetRequiredService<MainViewModel>().LoadAsync();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _services?.Dispose();
        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(
            $"An unexpected error occurred.\n\n{e.Exception.GetBaseException().Message}",
            "Transport Orders",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }
}
