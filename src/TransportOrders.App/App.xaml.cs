using System.IO;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;
using TransportOrders.App.Services;
using TransportOrders.App.ViewModels;
using TransportOrders.Data;

namespace TransportOrders.App;

public partial class App : Application
{
    private ServiceProvider? _services;
    private DispatcherTimer? _clockTimer;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var databasePath = DatabasePaths.GetDefaultDatabasePath();
        var dataFolder = Path.GetDirectoryName(databasePath)!;

        // Rolling daily log files in %LOCALAPPDATA%\TransportOrders\logs, kept for 14 days.
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            // Failed saves are logged once, with the order context, by the view model.
            .MinimumLevel.Override("Microsoft.EntityFrameworkCore.Update", LogEventLevel.Fatal)
            .WriteTo.File(Path.Combine(dataFolder, "logs", "app-.log"),
                rollingInterval: RollingInterval.Day, retainedFileCountLimit: 14)
            .CreateLogger();
        Log.Information("Starting, database {Path:l}", databasePath);
        DispatcherUnhandledException += OnDispatcherUnhandledException;

        // English by default; the choice made with the EN/DE switch is remembered.
        var settings = new SettingsStore(Path.Combine(dataFolder, "settings.json"));
        var localizer = Localizer.Instance;
        localizer.SetLanguage(settings.LoadLanguage() ?? "en");
        localizer.LanguageChanged += (_, _) => settings.SaveLanguage(localizer.Language);

        var services = new ServiceCollection();
        services.AddLogging(logging => logging.AddSerilog(dispose: true));
        services.AddTransportOrdersData(databasePath);
        services.AddSingleton<ILocalizer>(localizer);
        services.AddSingleton<ICurrentUser, WindowsCurrentUser>();
        services.AddSingleton(TimeZoneInfo.Local);
        services.AddSingleton<IUserMessageService, MessageBoxService>();
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();
        _services = services.BuildServiceProvider();

        var window = _services.GetRequiredService<MainWindow>();
        MainWindow = window;
        window.Show();

        var messages = _services.GetRequiredService<IUserMessageService>();
        try
        {
            var result = await _services.GetRequiredService<DatabaseInitializer>().InitializeAsync();
            if (result.LegacyBackupPath is { } backup)
            {
                Log.Warning("Database from version 1 moved to {Backup:l}", backup);
                messages.ShowInfo(localizer.Format("Info_LegacyBackup", backup));
            }
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Database initialization failed");
            messages.ShowError(localizer.Format("Error_Init", ex.GetBaseException().Message));
            Shutdown(1);
            return;
        }

        var viewModel = _services.GetRequiredService<MainViewModel>();
        await viewModel.LoadAsync();

        // Due times pass and shifts change even when nobody touches the data.
        _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _clockTimer.Tick += (_, _) => viewModel.RefreshClock();
        _clockTimer.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _clockTimer?.Stop();
        Log.Information("Exiting with code {Code}", e.ApplicationExitCode);
        _services?.Dispose();
        Log.CloseAndFlush();
        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error(e.Exception, "Unhandled exception");
        MessageBox.Show(
            Localizer.Instance.Format("Error_Unexpected", e.Exception.GetBaseException().Message),
            Localizer.Instance["App_Title"],
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }
}
