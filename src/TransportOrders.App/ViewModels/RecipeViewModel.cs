using System.Collections.ObjectModel;
using System.Globalization;
using System.Net;
using System.Net.Http;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TransportOrders.App.Services;
using TransportOrders.Core;
namespace TransportOrders.App.ViewModels;

public sealed record RecipeRow(OvenRecipeSnapshot Recipe, string Status);
public sealed record RecipeHistoryRow(string Time, string Actor, string Action, string Revision, string Parameters, string Reason);
public sealed partial class RecipeViewModel : ObservableObject
{
    private readonly PlantConnection _connection;
    private readonly ILocalizer _localizer;
    private readonly IUserMessageService _messages;
    private readonly Func<Task> _reload;
    private PlantClientSnapshot? _snapshot;
    private bool _refreshing;
    private Guid _draftVersion;
    private string? _draftId;
    private CultureInfo InputCulture => CultureInfo.GetCultureInfo(_localizer.Language == "de" ? "de-DE" : "en-US");
    public RecipeViewModel(PlantConnection connection, ILocalizer localizer, IUserMessageService messages, Func<Task> reload)
    { _connection = connection; _localizer = localizer; _messages = messages; _reload = reload;
      localizer.LanguageChanged += (_, _) => Refresh(_snapshot); }
    public ObservableCollection<RecipeRow> Recipes { get; } = [];
    public ObservableCollection<RecipeHistoryRow> History { get; } = [];
    [ObservableProperty] private RecipeRow? _selectedRecipe;
    [ObservableProperty] private string _identifier = "";
    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(ValidateCommand), nameof(ArchiveCommand))] private string _alloy = "6060";
    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(ValidateCommand), nameof(ArchiveCommand))] private string _temperature = "180";
    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(ValidateCommand), nameof(ArchiveCommand))] private string _heating = "2";
    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(ValidateCommand), nameof(ArchiveCommand))] private string _holding = "3";
    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(ValidateCommand), nameof(ArchiveCommand))] private string _cooling = "2";
    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(SaveCommand), nameof(ValidateCommand), nameof(ArchiveCommand))] private string _reason = "";
    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(NewCommand), nameof(SaveCommand), nameof(ValidateCommand), nameof(ArchiveCommand))] private bool _isBusy;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(IdentifierReadOnly))] private bool _isNew;
    public bool IdentifierReadOnly => !IsNew;
    [ObservableProperty] private string _status = "";
    public void Refresh(PlantClientSnapshot? snapshot)
    {
        _snapshot = snapshot;
        var selected = SelectedRecipe?.Recipe.Id;
        if (snapshot is not null)
        {
            _refreshing = true; Recipes.Clear();
            foreach (var recipe in snapshot.State.Recipes.OrderBy(r => r.Id)) Recipes.Add(new(recipe, _localizer[$"Recipe_Status_{recipe.Status}"]));
            SelectedRecipe = Recipes.FirstOrDefault(r => r.Recipe.Id == selected) ?? Recipes.FirstOrDefault(); _refreshing = false;
            if (!IsNew && _draftId is null) LoadSelection();
            ShowHistory();
        }
        Status = snapshot is null ? _localizer["OperatorApi_Unavailable"] : _localizer["Recipe_Guide"];
        NotifyCommands();
    }
    partial void OnSelectedRecipeChanged(RecipeRow? value)
    { if (!_refreshing) { IsNew = false; LoadSelection(); } }
    private void ShowHistory()
    {
        History.Clear();
        foreach (var e in SelectedRecipe?.Recipe.History ?? [])
            History.Add(new(e.AtUtc.ToLocalTime().ToString("g"), e.Actor, _localizer[$"Recipe_Event_{e.Action}"],
                e.Revision.ToString(), $"EN AW-{e.Parameters.Alloy} · {e.Parameters.TargetTemperature} °C · {e.Parameters.HeatingMinutes}/{e.Parameters.HoldingMinutes}/{e.Parameters.CoolingMinutes} min", e.Reason));
    }
    [RelayCommand]
    private void LoadSelection()
    {
        if (SelectedRecipe?.Recipe is not { } r) return;
        IsNew = false; _draftId = r.Id; _draftVersion = r.Version; Identifier = r.Id; Alloy = r.Alloy;
        Temperature = r.TargetTemperature.ToString(InputCulture); Heating = r.HeatingMinutes.ToString();
        Holding = r.HoldingMinutes.ToString(); Cooling = r.CoolingMinutes.ToString(); Reason = ""; ShowHistory(); NotifyCommands();
    }
    private bool CanManage() => !IsBusy && _snapshot?.Access.Roles.Contains("Supervisor") == true
        && _snapshot.Access.Permissions.Count > 0 && _snapshot.State.Recipes.All(r => r.Version != Guid.Empty);
    [RelayCommand(CanExecute = nameof(CanManage))]
    private void New()
    { IsNew = true; _draftId = null; _draftVersion = Guid.Empty; Identifier = ""; Alloy = "6060"; Temperature = "180";
      Heating = "2"; Holding = "3"; Cooling = "2"; Reason = ""; NotifyCommands(); }
    private bool HasReason() => !string.IsNullOrWhiteSpace(Reason) && Reason.Length <= 1000;
    private bool CanSave() => CanManage() && HasReason() && (IsNew || SelectedRecipe?.Recipe.Status is not RecipeStatus.Archived && _draftId is not null);
    private bool MatchesStoredParameters() => SelectedRecipe?.Recipe is { } r && Alloy.Trim() == r.Alloy
        && decimal.TryParse(Temperature, NumberStyles.Number, InputCulture, out var temperature) && temperature == r.TargetTemperature
        && int.TryParse(Heating, out var heating) && heating == r.HeatingMinutes
        && int.TryParse(Holding, out var holding) && holding == r.HoldingMinutes
        && int.TryParse(Cooling, out var cooling) && cooling == r.CoolingMinutes;
    private bool CanValidate() => CanManage() && HasReason() && !IsNew && MatchesStoredParameters() && SelectedRecipe?.Recipe.Status == RecipeStatus.Draft;
    private bool CanArchive() => CanManage() && HasReason() && !IsNew && MatchesStoredParameters() && SelectedRecipe?.Recipe.Status is RecipeStatus.Draft or RecipeStatus.Validated;
    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        if (!CanSave()) return;
        if (!decimal.TryParse(Temperature, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign | NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite,
            InputCulture, out var temperature) || !int.TryParse(Heating, out var heating) || !int.TryParse(Holding, out var holding) || !int.TryParse(Cooling, out var cooling))
        { _messages.ShowError(_localizer["Recipe_Invalid"]); return; }
        var id = IsNew ? Identifier.Trim().ToUpperInvariant() : _draftId!;
        await Send(id, IsNew ? "create" : "revise", new(Alloy.Trim(), temperature, heating, holding, cooling));
    }
    [RelayCommand(CanExecute = nameof(CanValidate))]
    private Task ValidateAsync() => CanValidate() ? Send(_draftId!, "validate", null) : Task.CompletedTask;
    [RelayCommand(CanExecute = nameof(CanArchive))]
    private Task ArchiveAsync() => CanArchive() ? Send(_draftId!, "archive", null) : Task.CompletedTask;
    private async Task Send(string id, string action, RecipeParameters? p)
    {
        IsBusy = true;
        try
        {
            await _connection.ChangeRecipeAsync(id, action, _draftVersion, p, Reason);
            IsNew = false; _draftId = id; await _reload();
            SelectedRecipe = Recipes.FirstOrDefault(r => r.Recipe.Id == id); LoadSelection();
        }
        catch (HttpRequestException ex) { _messages.ShowError(_localizer[ex.StatusCode == HttpStatusCode.BadRequest ? "Recipe_Invalid"
            : ex.StatusCode == HttpStatusCode.Conflict ? "OperatorApi_Conflict" : ex.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
            ? "OperatorApi_Unauthorized" : "OperatorApi_Unavailable"]); }
        catch (Exception ex) when (ex is TaskCanceledException or InvalidOperationException)
        { _messages.ShowError(_localizer["OperatorApi_Unavailable"]); }
        finally { IsBusy = false; NotifyCommands(); }
    }
    private void NotifyCommands()
    { NewCommand.NotifyCanExecuteChanged(); SaveCommand.NotifyCanExecuteChanged(); ValidateCommand.NotifyCanExecuteChanged(); ArchiveCommand.NotifyCanExecuteChanged(); }
}
