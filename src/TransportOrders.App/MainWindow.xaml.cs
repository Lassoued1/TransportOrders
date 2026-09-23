using System.Windows;
using System.Windows.Input;
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
    }

    // Ctrl+F: pure view concern (keyboard focus), so it stays in the code-behind.
    private void OnFindExecuted(object sender, ExecutedRoutedEventArgs e)
    {
        SearchBox.Focus();
        SearchBox.SelectAll();
    }
}
