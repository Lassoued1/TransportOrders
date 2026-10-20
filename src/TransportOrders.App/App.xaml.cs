using System.IO;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;
using TransportOrders.App.Services;
using TransportOrders.App.ViewModels;
using TransportOrders.Core;

namespace TransportOrders.App;

public partial class App : Application
{
    private readonly bool _startClient;
    public App() : this(true) { }
    internal App(bool startClient) => _startClient = startClient;
    private ServiceProvider? _services;
    private DispatcherTimer? _clockTimer;
    private Mutex? _singleInstance;

    protected override async void OnStartup(StartupEventArgs e)
    {
        if (!_startClient) return;
        base.OnStartup(e);

        _singleInstance = new Mutex(true, "Local\\TransportOrders.Extrusion", out var ownsSimulation);
        if (!ownsSimulation)
        {
            _singleInstance.Dispose();
            _singleInstance = null;
            MessageBox.Show("The extrusion application is already running in this Windows session.", "Transport Orders");
            Shutdown();
            return;
        }

        var connection = new PlantConnection(Environment.GetEnvironmentVariable("TRANSPORTORDERS_SERVICE_URL") ?? "http://localhost:5080", Environment.GetEnvironmentVariable("TRANSPORTORDERS_PLANT_ID"));
        var dataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TransportOrders.Client");

        // Client logs and preferences are independent of the server's database directory.
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .WriteTo.File(Path.Combine(dataFolder, "logs", "app-.log"),
                rollingInterval: RollingInterval.Day, retainedFileCountLimit: 14)
            .CreateLogger();
        Log.Information("Starting, central service {Address}", connection.Address);
        DispatcherUnhandledException += OnDispatcherUnhandledException;

        // English by default; the choice made with the EN/DE switch is remembered.
        var settings = new SettingsStore(Path.Combine(dataFolder, "settings.json"));
        var previousSettings = new SettingsStore(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "TransportOrders", "settings.json"));
        var localizer = Localizer.Instance;
        localizer.SetLanguage(settings.LoadLanguage() ?? previousSettings.LoadLanguage() ?? "en");
        settings.SaveLanguage(localizer.Language);
        localizer.LanguageChanged += (_, _) => settings.SaveLanguage(localizer.Language);

        var services = new ServiceCollection();
        services.AddLogging(logging => logging.AddSerilog(dispose: true));
        services.AddSingleton(connection);
        services.AddSingleton<IOrderRepository, RemotePlantRepository>();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<ILocalizer>(localizer);
        services.AddSingleton<ICurrentUser, WindowsCurrentUser>();
        services.AddSingleton(TimeZoneInfo.Local);
        services.AddSingleton<IUserMessageService, MessageBoxService>();
        services.AddSingleton<IDialogService, WpfDialogService>();
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<ExtrusionSimulator>();
        services.AddSingleton<IEquipmentControlClient>(new EquipmentControlClient("", localizer, connection: connection));
        services.AddSingleton<IOperatorCommandClient>(new OperatorCommandClient("", localizer, connection: connection));
        services.AddSingleton<MainWindow>();
        _services = services.BuildServiceProvider();

        _services.GetRequiredService<MainViewModel>().InitializeExtrusion(_services.GetRequiredService<ExtrusionSimulator>(),
            _services.GetRequiredService<IEquipmentControlClient>());
        _services.GetRequiredService<MainViewModel>().SetOperatorAccess(null);

        var window = _services.GetRequiredService<MainWindow>();
        MainWindow = window;
        window.IsEnabled = false;
        window.Show();

        var viewModel = _services.GetRequiredService<MainViewModel>();
        var alarms = new AlarmViewModel(connection, localizer, _services.GetRequiredService<IUserMessageService>(), viewModel.LoadAsync);
        viewModel.InitializeAlarms(alarms);
        var recipes = new RecipeViewModel(connection, localizer, _services.GetRequiredService<IUserMessageService>(), viewModel.LoadAsync);
        viewModel.InitializeRecipes(recipes);
        connection.Changed += snapshot => Dispatcher.Invoke(() =>
        {
            alarms.Refresh(snapshot);
            recipes.Refresh(snapshot);
            if (snapshot is not null) viewModel.SetPlantTimeZone(snapshot.TimeZoneId);
            viewModel.SetOperatorAccess(snapshot?.Access);
            var runtime = snapshot?.Runtime;
            viewModel.Extrusion?.SetServiceStatus(runtime is null ? "Unavailable"
                : runtime.LastError is not null ? "Error" : runtime.Enabled && runtime.Running ? "Running" : "Stopped",
                runtime?.LastError ?? runtime?.LastTickUtc?.ToLocalTime().ToString("HH:mm:ss") ?? "");
        });
        await viewModel.LoadAsync();
        window.IsEnabled = true;
        // Due times pass and shifts change even when nobody touches the data.
        _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        var refreshing = false;
        _clockTimer.Tick += async (_, _) =>
        {
            if (refreshing || viewModel.IsBusy || viewModel.Extrusion?.IsBusy == true) return;
            refreshing = true;
            try
            {
                await viewModel.LoadAsync(silent: true);
            }
            finally { refreshing = false; }
        };
        _clockTimer.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _clockTimer?.Stop();
        _singleInstance?.ReleaseMutex();
        _singleInstance?.Dispose();
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
