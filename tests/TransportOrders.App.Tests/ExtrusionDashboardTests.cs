using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using TransportOrders.App.Services;
using TransportOrders.App.ViewModels;
using TransportOrders.Core;
using TransportOrders.Data;

namespace TransportOrders.App.Tests;

public class ExtrusionDashboardTests
{
    [Fact]
    public void Dashboard_loads_real_seed_and_renders_on_an_STA_thread()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            dispatcher.BeginInvoke(new Action(async () =>
            {
                var path = Path.Combine(Path.GetTempPath(), $"extrusion-preview-{Guid.NewGuid():N}.db");
                try
                {
                    using var services = new ServiceCollection().AddTransportOrdersData(path).BuildServiceProvider();
                    await services.GetRequiredService<DatabaseInitializer>().InitializeAsync();
                    var repository = services.GetRequiredService<IOrderRepository>();
                    await repository.StartOvenCycleAsync("OVEN-01", "DEMO-6060", ["AL-26031-K01", "AL-26031-K02"], "preview", DateTime.UtcNow);
                    var cycle = (await repository.GetPlantStateAsync()).Cycles.Single();
                    await repository.AdvanceOvenCycleAsync(cycle.Id, cycle.Version, 3, "preview", DateTime.UtcNow);
                    var packingBasket = (await repository.GetPlantStateAsync()).Baskets.Single(b => b.Number == "AL-26052-K01");
                    await repository.StartPackingAsync(packingBasket.Number, packingBasket.Version, "preview", DateTime.UtcNow);
                    var package = (await repository.GetPlantStateAsync()).Packages.Single();
                    await repository.CompletePackingAsync(package.Number, package.Version, "preview", DateTime.UtcNow);
                    // Load the real resources without starting a client against the installed service.
                    var app = new App(startClient: false);
                    app.InitializeComponent();
                    var vm = new MainViewModel(repository, new FakeMessages(), new FakeDialogs(), Localizer.Instance,
                        new FakeUser("preview"), TimeProvider.System, TimeZoneInfo.Utc, NullLogger<MainViewModel>.Instance,
                        new TestOperatorClient(repository, TimeProvider.System, "preview"));
                    vm.InitializeExtrusion(new ExtrusionSimulator(repository, TimeProvider.System));
                    await vm.LoadAsync();
                    Assert.True(vm.Extrusion!.InspectPackageCommand.CanExecute(null));
                    vm.Extrusion.VisualPassed = false;
                    vm.Extrusion.InspectionReason = "Packaging damaged";
                    await vm.Extrusion.InspectPackageCommand.ExecuteAsync(null);
                    Assert.Equal(PackageStatus.Rejected, vm.Extrusion.SelectedPackage!.StatusValue);
                    vm.Extrusion.VisualPassed = true;
                    vm.Extrusion.InspectionReason = "Repacked and checked";
                    await vm.Extrusion.InspectPackageCommand.ExecuteAsync(null);
                    Assert.Contains("PACKAGE PK-", vm.Extrusion.SelectedPackage!.Label);
                    await new ExtrusionSimulator(repository, TimeProvider.System).StepAsync();
                    packingBasket = (await repository.GetPlantStateAsync()).Baskets.Single(b => b.Number == packingBasket.Number);
                    await repository.StartEmptyReturnAsync(packingBasket.Number, packingBasket.Version, "PRESS-01", "preview", DateTime.UtcNow);
                    var returning = (await repository.GetPlantStateAsync()).Returns.Single();
                    await repository.CompleteEmptyReturnAsync(returning.Id, returning.Version, "preview", DateTime.UtcNow);
                    await vm.LoadAsync();
                    vm.Extrusion!.SetServiceStatus("Running", "12:34:56");
                    vm.SetOperatorAccess(new("PLANT\\preview", ["Supervisor"], PlantPermissions.All, path));
                    using var previewConnection = new PlantConnection("https://localhost");
                    var alarmVm = new AlarmViewModel(previewConnection, Localizer.Instance, new FakeMessages(), () => Task.CompletedTask);
                    vm.InitializeAlarms(alarmVm);
                    var previewAlarm = PlantAlarmSnapshot.From(new PlantAlarm(new("preview-fault", AlarmCode.EquipmentFault, "OVEN-01", ""), DateTime.UtcNow));
                    alarmVm.Refresh(new(1, "preview", "Preview", "UTC", DateTime.UtcNow,
                        PlantStateSnapshot.From(await repository.GetPlantStateAsync()), new("Preview", ["Supervisor"], PlantPermissions.All, ""),
                        new(true, true, DateTime.UtcNow, null), [previewAlarm]));
                    var window = new MainWindow(vm);
                    var content = (FrameworkElement)window.Content;
                    content.Measure(new Size(1360, 1260));
                    content.Arrange(new Rect(0, 0, 1360, 1260));
                    content.UpdateLayout();
                    Assert.Equal(7, vm.Extrusion!.Stations.Count);
                    Assert.Equal(12, vm.Extrusion.Baskets.Count);
                    Assert.Equal(0, vm.Extrusion.MovingCount);
                    Assert.Equal(7, vm.Extrusion.Equipment.Count);
                    Assert.Contains("Holding", vm.Extrusion.Stations.Single(s => s.Id == "OVEN-01").Process);
                    Assert.DoesNotContain(vm.Extrusion.Stations, s => s.Kind.StartsWith('['));
                    var bitmap = new RenderTargetBitmap(1360, 1260, 96, 96, PixelFormats.Pbgra32);
                    bitmap.Render(content);
                    if (Environment.GetEnvironmentVariable("TRANSPORTORDERS_PREVIEW_PATH") is { Length: > 0 } output)
                    {
                        var encoder = new PngBitmapEncoder();
                        encoder.Frames.Add(BitmapFrame.Create(bitmap));
                        using var stream = File.Create(output);
                        encoder.Save(stream);
                    }
                    var packing = new TransportOrders.App.Views.PackingOverview { DataContext = vm.Extrusion };
                    var languageSelector = (System.Windows.Controls.ListBox)window.FindName("GlobalLanguageSelector");
                    Assert.Contains(vm.Languages, l => l.Key == "de" && l.Label == "Deutsch");
                    languageSelector.SelectedValue = "de";
                    await Dispatcher.Yield(DispatcherPriority.Background);
                    content.UpdateLayout();
                    Assert.Equal("de", vm.SelectedLanguage);
                    Assert.Equal("de", Localizer.Instance.Language);
                    Assert.Contains(Localizer.Instance["Plant_Phase_Holding"], vm.Extrusion.Stations.Single(s => s.Id == "OVEN-01").Process);
                    Assert.Contains("Zentraler Dienst", vm.Extrusion.ServiceStatus);
                    var alarmOverview = new TransportOrders.App.Views.AlarmOverview { DataContext = alarmVm };
                    var recipeVm = new RecipeViewModel(previewConnection, Localizer.Instance, new FakeMessages(), () => Task.CompletedTask);
                    recipeVm.Refresh(new(1, "preview", "Preview", "UTC", DateTime.UtcNow,
                        PlantStateSnapshot.From(await repository.GetPlantStateAsync()), new("Preview", ["Supervisor"], PlantPermissions.All, ""),
                        new(true, true, DateTime.UtcNow, null)));
                    Assert.Equal(3, recipeVm.Recipes.Count);
                    Assert.DoesNotContain(recipeVm.Recipes, r => r.Status.StartsWith('['));
                    var recipeOverview = new TransportOrders.App.Views.RecipeOverview { DataContext = recipeVm };
                    recipeOverview.Measure(new Size(1360, 900)); recipeOverview.Arrange(new Rect(0, 0, 1360, 900)); recipeOverview.UpdateLayout();
                    if (Environment.GetEnvironmentVariable("TRANSPORTORDERS_RECIPE_PREVIEW_PATH") is { Length: > 0 } recipeOutput)
                    {
                        var rendered = new RenderTargetBitmap(1360, 900, 96, 96, PixelFormats.Pbgra32); rendered.Render(recipeOverview);
                        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(rendered));
                        using var stream = File.Create(recipeOutput); encoder.Save(stream);
                    }
                    alarmOverview.Measure(new Size(1360, 1000)); alarmOverview.Arrange(new Rect(0, 0, 1360, 1000)); alarmOverview.UpdateLayout();
                    Assert.Equal("Anlagenstörung", alarmVm.Alarms.Single().Message);
                    if (Environment.GetEnvironmentVariable("TRANSPORTORDERS_ALARM_PREVIEW_PATH") is { Length: > 0 } alarmOutput)
                    {
                        var rendered = new RenderTargetBitmap(1360, 1000, 96, 96, PixelFormats.Pbgra32); rendered.Render(alarmOverview);
                        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(rendered));
                        using var stream = File.Create(alarmOutput); encoder.Save(stream);
                    }
                    if (Environment.GetEnvironmentVariable("TRANSPORTORDERS_GERMAN_PREVIEW_PATH") is { Length: > 0 } germanOutput)
                    {
                        var germanBitmap = new RenderTargetBitmap(1360, 1260, 96, 96, PixelFormats.Pbgra32);
                        germanBitmap.Render(content);
                        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(germanBitmap));
                        using var stream = File.Create(germanOutput); encoder.Save(stream);
                    }
                    languageSelector.SelectedValue = "en";
                    await Dispatcher.Yield(DispatcherPriority.Background);
                    packing.Measure(new Size(1360, 1000)); packing.Arrange(new Rect(0, 0, 1360, 1000)); packing.UpdateLayout();
                    if (Environment.GetEnvironmentVariable("TRANSPORTORDERS_PACKING_PREVIEW_PATH") is { Length: > 0 } packingOutput)
                    {
                        var packingBitmap = new RenderTargetBitmap(1360, 1000, 96, 96, PixelFormats.Pbgra32);
                        packingBitmap.Render(packing);
                        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(packingBitmap));
                        using var stream = File.Create(packingOutput); encoder.Save(stream);
                    }
                    vm.Extrusion.ReloadQuantity = 17;
                    Assert.True(vm.Extrusion.ReloadBasketCommand.CanExecute(null));
                    await vm.Extrusion.ReloadBasketCommand.ExecuteAsync(null);
                    Assert.Empty(vm.Extrusion.EmptyBaskets);
                    Assert.Contains(vm.VisibleOrders, o => o.Order.BasketContentSequence == 2 && o.Order.PieceNumber == packingBasket.Number);
                    Assert.NotEmpty(vm.Extrusion.ScheduledActions);
                    Assert.DoesNotContain(vm.Extrusion.ScheduledActions, r => r.Action.StartsWith('[') || r.Reason.StartsWith('['));
                    Assert.Contains(vm.Extrusion.ScheduleBlocks, r => r.Reference == "AL-26052-K03");
                    var schedule = new TransportOrders.App.Views.ScheduleOverview { DataContext = vm.Extrusion };
                    schedule.Measure(new Size(1360, 960)); schedule.Arrange(new Rect(0, 0, 1360, 960)); schedule.UpdateLayout();
                    if (Environment.GetEnvironmentVariable("TRANSPORTORDERS_SCHEDULE_PREVIEW_PATH") is { Length: > 0 } scheduleOutput)
                    {
                        var scheduleBitmap = new RenderTargetBitmap(1360, 960, 96, 96, PixelFormats.Pbgra32);
                        scheduleBitmap.Render(schedule);
                        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(scheduleBitmap));
                        using var stream = File.Create(scheduleOutput); encoder.Save(stream);
                    }
                    package = (await repository.GetPlantStateAsync()).Packages.Single();
                    await repository.StartPackageShippingAsync(package.Number, package.Version, "preview", DateTime.UtcNow);
                    await vm.LoadAsync();
                    vm.Extrusion.PlcSuppressReplies = true;
                    vm.Extrusion.PlcReason = "Demonstrate lost acknowledgement";
                    Assert.True(vm.Extrusion.SetPlcSettingsCommand.CanExecute(null));
                    await vm.Extrusion.SetPlcSettingsCommand.ExecuteAsync(null);
                    await vm.Extrusion.StepCommand.ExecuteAsync(null);
                    Assert.Contains(vm.Extrusion.PlcCommands, c => c.StatusValue == PlcCommandStatus.AwaitingAck);
                    Assert.DoesNotContain(vm.Extrusion.PlcCommands, c => c.Status.StartsWith('['));
                    var plc = new TransportOrders.App.Views.PlcOverview { DataContext = vm.Extrusion };
                    plc.Measure(new Size(1360, 960)); plc.Arrange(new Rect(0, 0, 1360, 960)); plc.UpdateLayout();
                    if (Environment.GetEnvironmentVariable("TRANSPORTORDERS_PLC_PREVIEW_PATH") is { Length: > 0 } plcOutput)
                    {
                        var plcBitmap = new RenderTargetBitmap(1360, 960, 96, 96, PixelFormats.Pbgra32);
                        plcBitmap.Render(plc);
                        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(plcBitmap));
                        using var stream = File.Create(plcOutput); encoder.Save(stream);
                    }
                    vm.Extrusion.PlcSuppressReplies = false;
                    vm.Extrusion.PlcReason = "Restore reply delivery";
                    await vm.Extrusion.SetPlcSettingsCommand.ExecuteAsync(null);
                    await vm.Extrusion.StepCommand.ExecuteAsync(null);
                    Assert.Equal(1, vm.Extrusion.ShippedCount);
                    window.Close();
                }
                catch (Exception ex) { failure = ex; }
                finally
                {
                    Localizer.Instance.SetLanguage("en");
                    SqliteConnection.ClearAllPools();
                    foreach (var suffix in new[] { "", "-wal", "-shm" }) File.Delete(path + suffix);
                    dispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
                }
            }));
            Dispatcher.Run();
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(60)), "WPF layout timed out.");
        Assert.Null(failure);
    }
}
