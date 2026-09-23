using System.Windows.Data;
using System.Windows.Markup;

namespace TransportOrders.App.Services;

/// <summary>XAML shorthand for a localized text: <c>Text="{svc:Tr App_Title}"</c>.</summary>
[MarkupExtensionReturnType(typeof(object))]
public sealed class TrExtension : MarkupExtension
{
    public TrExtension()
    {
    }

    public TrExtension(string key) => Key = key;

    public string Key { get; set; } = string.Empty;

    public override object ProvideValue(IServiceProvider serviceProvider) =>
        new Binding($"[{Key}]") { Source = Localizer.Instance, Mode = BindingMode.OneWay }.ProvideValue(serviceProvider);
}
