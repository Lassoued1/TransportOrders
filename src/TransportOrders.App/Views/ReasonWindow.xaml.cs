using System.Windows;

namespace TransportOrders.App.Views;

public partial class ReasonWindow : Window
{
    public ReasonWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => ReasonBox.Focus();
    }

    private void OnOk(object sender, RoutedEventArgs e) => DialogResult = true;
}
