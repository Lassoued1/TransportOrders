using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Extensions.Logging.Abstractions;
using TransportOrders.App;
using TransportOrders.App.Services;
using TransportOrders.App.ViewModels;
using TransportOrders.Core;

internal sealed record Scene(string File, string Chapter, string Title, string Caption, double Seconds);
internal sealed class Messages : IUserMessageService
{
    public void ShowError(string message) => throw new InvalidOperationException(message);
    public void ShowInfo(string message) => Console.WriteLine(message);
    public bool Confirm(string message) => true;
}
internal static class Program
{
    private static readonly List<Scene> Scenes = [];
    private static MainWindow Window = null!;
    private static TabControl Tabs = null!;
    private static string Output = "";
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length != 3) { Console.Error.WriteLine("Usage: DemoVideo <loopback-api> <plant-id> <new-output-directory>"); return 2; }
        var address = PlantConnection.ValidateAddress(args[0]);
        if (!address.IsLoopback || address.Port != 5097) throw new InvalidOperationException("Use the isolated video API on localhost:5097.");
        Output = Path.GetFullPath(args[2]); Directory.CreateDirectory(Output);
        var app = new App(); app.InitializeComponent();
        var dispatcher = Dispatcher.CurrentDispatcher;
        Exception? failure = null;
        dispatcher.BeginInvoke(new Action(async () =>
        {
            try { await Export(args[0], args[1]); }
            catch (Exception ex) { failure = ex; Console.Error.WriteLine(ex); }
            finally { dispatcher.InvokeShutdown(); }
        }));
        Dispatcher.Run();
        return failure is null ? 0 : 1;
    }
    private static T? Find<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T typed) return typed;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            if (Find<T>(VisualTreeHelper.GetChild(root, i)) is { } child) return child;
        return null;
    }
    private static async Task Capture(int tab, string chapter, string title, string caption, double seconds = 6)
    {
        Tabs.SelectedIndex = tab;
        await Dispatcher.Yield(DispatcherPriority.Background);
        var content = (FrameworkElement)Window.Content;
        content.Measure(new Size(1600, 820)); content.Arrange(new Rect(0, 0, 1600, 820)); content.UpdateLayout();
        var rendered = new RenderTargetBitmap(1600, 820, 96, 96, PixelFormats.Pbgra32); rendered.Render(content);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(rendered));
        var file = $"ui-{Scenes.Count + 1:000}.png";
        using (var stream = File.Create(Path.Combine(Output, file))) encoder.Save(stream);
        Scenes.Add(new(file, chapter, title, caption, seconds)); Console.WriteLine($"Scene {Scenes.Count}: {title}");
    }
    private static async Task Export(string address, string plantId)
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("de-DE");
        var localizer = Localizer.Instance; localizer.SetLanguage("de");
        using var connection = new PlantConnection(address, plantId);
        var repository = new RemotePlantRepository(connection);
        var messages = new Messages();
        using var commands = new OperatorCommandClient("", localizer, connection: connection);
        using var controls = new EquipmentControlClient("", localizer, connection: connection);
        var vm = new MainViewModel(repository, messages, new WpfDialogService(), localizer, new WindowsCurrentUser(),
            TimeProvider.System, TimeZoneInfo.Local, NullLogger<MainViewModel>.Instance, commands);
        vm.InitializeExtrusion(new ExtrusionSimulator(repository, TimeProvider.System), controls);
        var alarms = new AlarmViewModel(connection, localizer, messages, vm.LoadAsync); vm.InitializeAlarms(alarms);
        var recipes = new RecipeViewModel(connection, localizer, messages, vm.LoadAsync); vm.InitializeRecipes(recipes);
        connection.Changed += snapshot =>
        {
            if (Application.Current.Dispatcher.CheckAccess()) UpdateSnapshot(snapshot);
            else Application.Current.Dispatcher.Invoke(() => UpdateSnapshot(snapshot));
        };
        void UpdateSnapshot(PlantClientSnapshot? snapshot)
        {
            if (snapshot is null) { vm.SetOperatorAccess(null); return; }
            if (snapshot.PlantId != plantId || snapshot.OperatingMode != "Demo" || snapshot.Runtime.Enabled)
                throw new InvalidOperationException("Refusing a non-isolated or automatically driven plant.");
            alarms.Refresh(snapshot); recipes.Refresh(snapshot);
            vm.SetOperatorAccess(snapshot.Access with { User = "Demo / Schichtleitung" });
            vm.Extrusion!.SetServiceStatus("Stopped", "Isolierte Demonstration");
        }
        await vm.LoadAsync();
        Window = new MainWindow(vm);
        var content = (FrameworkElement)Window.Content;
        content.Measure(new Size(1600, 820)); content.Arrange(new Rect(0, 0, 1600, 820)); content.UpdateLayout();
        Tabs = Find<TabControl>(content) ?? throw new InvalidOperationException("Application tabs missing.");
        await Capture(0, "01 · Materialfluss", "Aluminiumextrusion im Überblick",
            "Pressen, Alterungsöfen und Verpackung: 12 Körbe mit nachvollziehbaren Positionen.", 8);
        await Capture(2, "01 · Materialfluss", "Produktionsplanung und Blockierungen",
            "Der Plan zeigt zulässige nächste Aktionen. Qualitätssperren und Kapazitäten bleiben wirksam.", 7);
        await commands.StepAsync(); await vm.LoadAsync();
        await Capture(0, "01 · Materialfluss", "Ein Transport wird bestätigt",
            "Eine Reservierung ist noch keine Ankunft. Die bestätigte Bewegung aktualisiert Position und Historie.");

        var seen = new HashSet<OvenPhase>();
        for (var step = 0; step < 140; step++)
        {
            var state = connection.Snapshot!.State;
            foreach (var phase in new[] { OvenPhase.Heating, OvenPhase.Holding, OvenPhase.Cooling, OvenPhase.Completed })
                if (!seen.Contains(phase) && state.Cycles.Any(c => c.EquipmentId == "OVEN-01" && c.Phase == phase))
                {
                    seen.Add(phase);
                    await Capture(0, "02 · Ofenbehandlung", phase switch
                    { OvenPhase.Heating => "Aufheizen", OvenPhase.Holding => "Halten", OvenPhase.Cooling => "Abkühlen", _ => "Behandlung abgeschlossen" },
                        "Ofenzyklen übernehmen Rezept und Beladung. Zeiten und Temperaturen werden hier simuliert.", 5);
                }
            if (seen.Count == 4 && state.Packages.Any(p => p.Status == PackageStatus.AwaitingInspection)) break;
            var result = await commands.StepAsync(); await vm.LoadAsync();
            if (result.Action == SimulationAction.Waiting) throw new InvalidOperationException("No package reached inspection.");
        }
        var extrusion = vm.Extrusion!;
        extrusion.SelectedPackage = extrusion.Packages.First(p => p.StatusValue == PackageStatus.AwaitingInspection);
        var packageNumber = extrusion.SelectedPackage.Number;
        extrusion.VisualPassed = false; extrusion.QuantityVerified = true;
        extrusion.InspectionReason = "Verpackung beschädigt - Nacharbeit erforderlich";
        await Capture(1, "03 · Qualität und Verpackung", "Qualitätsprüfung vor der Freigabe",
            "Sichtprüfung und Mengenkontrolle bestimmen, ob das Paket freigegeben werden darf.", 7);
        await extrusion.InspectPackageCommand.ExecuteAsync(null);
        if (extrusion.SelectedPackage?.StatusValue != PackageStatus.Rejected) throw new InvalidOperationException("Rejection missing.");
        await Capture(1, "03 · Qualität und Verpackung", "Paket gesperrt",
            "Eine fehlgeschlagene Prüfung sperrt die Freigabe. Der Prüfgrund wird gespeichert.");
        extrusion.VisualPassed = true; extrusion.InspectionReason = "Nacharbeit abgeschlossen - Sichtprüfung und Menge in Ordnung";
        await extrusion.InspectPackageCommand.ExecuteAsync(null);
        if (extrusion.SelectedPackage?.StatusValue != PackageStatus.Ready) throw new InvalidOperationException("Approval missing.");
        await Capture(1, "03 · Qualität und Verpackung", "Freigabe und rückverfolgbares Etikett",
            "Nach erfolgreicher erneuter Prüfung ist das Paket freigegeben. Das Etikett liegt als Text vor.", 7);

        var oven = connection.Snapshot!.State.Equipment.First(e => e.Id == "OVEN-01");
        await controls.SetControlAsync(oven.Id, oven.Version, EquipmentMode.Automatic, EquipmentCondition.Fault, "Demonstration einer Anlagenstörung");
        for (var poll = 0; poll < 15; poll++)
        { await vm.LoadAsync(); if (alarms.Alarms.Any(a => a.Alarm.IsActive && a.Alarm.Source == "OVEN-01")) break; await Task.Delay(300); }
        alarms.SelectedAlarm = alarms.Alarms.First(a => a.Alarm.IsActive && a.Alarm.Source == "OVEN-01");
        alarms.Reason = "Störung geprüft - Instandhaltung informiert";
        await Capture(4, "04 · Störungen und Alarmierung", "Anlagenstörung sichtbar machen",
            "Die Meldung wird dauerhaft gespeichert. Eine Quittierung dokumentiert die Kenntnisnahme.", 7);
        await alarms.AcknowledgeCommand.ExecuteAsync(null);
        await Capture(4, "04 · Störungen und Alarmierung", "Quittiert bedeutet nicht behoben",
            "Der Alarm bleibt aktiv, solange die Störung besteht. Benutzer und Kommentar stehen im Verlauf.", 7);
        oven = connection.Snapshot!.State.Equipment.First(e => e.Id == "OVEN-01");
        await controls.SetControlAsync(oven.Id, oven.Version, EquipmentMode.Automatic, EquipmentCondition.Operational, "Störung im Demonstrationslauf behoben");
        await vm.LoadAsync();

        recipes.NewCommand.Execute(null); recipes.Identifier = "DEMO-VIDEO-6060"; recipes.Alloy = "6060";
        recipes.Temperature = "185,5"; recipes.Heating = "2"; recipes.Holding = "4"; recipes.Cooling = "3";
        recipes.Reason = "Neues Rezept für den Simulationsversuch";
        await Capture(5, "05 · Ofenrezepte", "Ein neues Rezept vorbereiten",
            "Legierung, Temperatur sowie Aufheiz-, Halte- und Abkühlzeit eingeben. Änderungen benötigen einen Grund.", 8);
        await recipes.SaveCommand.ExecuteAsync(null);
        if (recipes.SelectedRecipe?.Recipe.Status != RecipeStatus.Draft) throw new InvalidOperationException("Draft missing.");
        recipes.Reason = "Parameter geprüft - für Simulation freigegeben";
        await Capture(5, "05 · Ofenrezepte", "Entwurf speichern und prüfen",
            "Ein Entwurf startet keinen Ofenzyklus. Erst die Schichtleitung validiert die gespeicherten Parameter.", 7);
        await recipes.ValidateCommand.ExecuteAsync(null);
        if (recipes.SelectedRecipe?.Recipe.Status != RecipeStatus.Validated) throw new InvalidOperationException("Validation missing.");
        await Capture(5, "05 · Ofenrezepte", "Für die Simulation validiert",
            "Revision 1 ist verwendbar. Der Verlauf enthält Parameter, Benutzer, Zeitpunkt und Freigabegrund.", 7);
        recipes.Temperature = "190"; recipes.Reason = "Temperatur für einen weiteren Simulationsversuch angepasst";
        await recipes.SaveCommand.ExecuteAsync(null);
        if (recipes.SelectedRecipe?.Recipe.Revision != 2) throw new InvalidOperationException("Revision missing.");
        await Capture(5, "05 · Ofenrezepte", "Änderung erzeugt Revision 2",
            "Die neue Revision ist wieder ein Entwurf. Bestehende Ofenzyklen behalten ihre ursprünglichen Parameter.", 8);

        // Demonstrate an actual pending acknowledgement without manufacturing any telemetry.
        extrusion.PlcSuppressReplies = true; extrusion.PlcReason = "SPS-Antworten im Demonstrationslauf unterdrücken";
        await extrusion.SetPlcSettingsCommand.ExecuteAsync(null);
        for (var step = 0; step < 35; step++)
        { await commands.StepAsync(); await vm.LoadAsync(); if (extrusion.PlcCommands.Any(c => c.StatusValue == PlcCommandStatus.AwaitingAck)) break; }
        extrusion.SelectedPlcCommand = extrusion.PlcCommands.First(c => c.StatusValue == PlcCommandStatus.AwaitingAck);
        await Capture(3, "06 · SPS-Kommunikation", "Ausbleibende Rückmeldung erkennen",
            "Die Antwort ist im Simulator unterdrückt. Auftrag und Zielreservierung bleiben erhalten.", 7);
        await Task.Delay(TimeSpan.FromSeconds(16));
        await commands.StepAsync(); await vm.LoadAsync();
        extrusion.SelectedPlcCommand = extrusion.PlcCommands.First(c => c.StatusValue == PlcCommandStatus.TimedOut);
        await Capture(3, "06 · SPS-Kommunikation", "Zeitüberschreitung ohne Blindflug",
            "Nach 15 Sekunden: Zeitüberschreitung. Kein automatischer Neuversand; die Reservierung bleibt bestehen.", 8);
        extrusion.PlcSuppressReplies = false; extrusion.PlcReason = "SPS-Antworten wieder zulassen";
        await extrusion.SetPlcSettingsCommand.ExecuteAsync(null);
        await commands.StepAsync(); await vm.LoadAsync();
        await Capture(3, "06 · SPS-Kommunikation", "Späte Antwort sicher übernehmen",
            "Eine gültige späte Rückmeldung kann den Auftrag abschließen, ohne den Transport erneut auszuführen.", 7);

        for (var step = 0; step < 250; step++)
        { var result = await commands.StepAsync(); if (result.Action == SimulationAction.Waiting) break; }
        await vm.LoadAsync();
        if (extrusion.ShippedCount != 10 || extrusion.EmptyBaskets.Count != 10) throw new InvalidOperationException("Complete flow failed.");
        await Capture(0, "07 · Ergebnis und Wiederverwendung", "Materialfluss abgeschlossen",
            "10 Pakete sind versendet. 10 leere Körbe kehren zurück. Gesperrtes und storniertes Material bleibt stehen.", 8);
        extrusion.SelectedPackage = extrusion.Packages.First(p => p.Number == packageNumber);
        extrusion.SelectedEmptyBasket = extrusion.EmptyBaskets.First();
        await Capture(1, "07 · Ergebnis und Wiederverwendung", "Leere Körbe erneut beladen",
            "Die Rückkehr ist dokumentiert. Eine neue Beladung erhält eine eigene Sequenz und einen neuen Transportauftrag.", 7);
        extrusion.ReloadQuantity = 17;
        await extrusion.ReloadBasketCommand.ExecuteAsync(null);
        await Capture(2, "07 · Ergebnis und Wiederverwendung", "Neuer Auftrag für die nächste Beladung",
            "Der Plan wird neu berechnet. Eine frühere Ofenbehandlung gilt nicht für die neue Ladung.", 7);
        vm.SelectedOrder = vm.VisibleOrders.FirstOrDefault(r => r.Order.BasketContentSequence == 2);
        await Capture(6, "08 · Rückverfolgung", "Aufträge und Historie nachvollziehen",
            "Korb, Materiallos, Beladungsfolge und Bewegungen bleiben über den gesamten Ablauf verknüpft.", 8);
        await Capture(0, "08 · Rückverfolgung", "Bereit für weitere Simulationsszenarien",
            "Gezeigt wurde die echte deutsche WPF-Oberfläche. Anlagen, Temperaturen und SPS-Antworten sind simuliert.", 8);
        File.WriteAllText(Path.Combine(Output, "scenes.json"), JsonSerializer.Serialize(Scenes, new JsonSerializerOptions { WriteIndented = true }));
    }
}
