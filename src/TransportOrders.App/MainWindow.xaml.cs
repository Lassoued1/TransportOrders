using System.Windows;
using TransportOrders.App.ViewModels;

namespace TransportOrders.App;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
