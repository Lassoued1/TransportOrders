using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using TransportOrders.App.ViewModels;

namespace TransportOrders.App;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        // Never open taller or wider than the usable screen area (e.g. laptops at 150 % scaling).
        Height = Math.Min(Height, SystemParameters.WorkArea.Height);
        Width = Math.Min(Width, SystemParameters.WorkArea.Width);

        var simulationTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        simulationTimer.Tick += async (_, _) =>
        {
            if (!viewModel.IsBusy && viewModel.Extrusion is { IsRunning: true, IsBusy: false } simulation)
                await simulation.StepCommand.ExecuteAsync(null);
        };
        Loaded += (_, _) => simulationTimer.Start();
        Closed += (_, _) => simulationTimer.Stop();
    }

    // Ctrl+F: pure view concern (keyboard focus), so it stays in the code-behind.
    private void OnFindExecuted(object sender, ExecutedRoutedEventArgs e)
    {
        SearchBox.Focus();
        SearchBox.SelectAll();
    }
}
