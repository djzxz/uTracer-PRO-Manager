using System.Collections.ObjectModel;
using uTracerProManager.Core.Models;
using uTracerProManager.Services;

namespace uTracerProManager.AvaloniaApp.ViewModels;

public sealed partial class MainWindowViewModel
{
    private ProfileWorkQueueService _profileWorkQueue = null!;
    private bool _profileWorkQueueInitializedV140;
    private string _profileQueueCategoryV140 = ProfileWorkQueueCategories.All;
    private string _profileQueueSearchV140 = string.Empty;
    private ProfileWorkQueueItem? _selectedProfileQueueItemV140;
    private string _profileQueueSummaryV140 = "Kolejka nie została jeszcze policzona.";
    private string _profileQueueDetailV140 = string.Empty;
    private string _profileQueueDisplayedV140 = "0 wyświetlonych";

    public ObservableCollection<ProfileWorkQueueItem> ProfileWorkQueue { get; } = new();
    public IReadOnlyList<string> ProfileQueueCategories { get; } = ProfileWorkQueueCategories.AllOptions;

    public AsyncRelayCommand RefreshProfileWorkQueueCommand { get; private set; } = null!;
    public AsyncRelayCommand OpenProfileWorkQueueItemCommand { get; private set; } = null!;
    public AsyncRelayCommand OpenNextProfileWorkQueueItemCommand { get; private set; } = null!;

    public string ProfileQueueCategory
    {
        get => _profileQueueCategoryV140;
        set
        {
            if (SetProperty(ref _profileQueueCategoryV140, value))
                _ = LoadProfileWorkQueueListSafeV140Async();
        }
    }

    public string ProfileQueueSearch
    {
        get => _profileQueueSearchV140;
        set
        {
            if (SetProperty(ref _profileQueueSearchV140, value))
                _ = LoadProfileWorkQueueListSafeV140Async();
        }
    }

    public ProfileWorkQueueItem? SelectedProfileQueueItem
    {
        get => _selectedProfileQueueItemV140;
        set
        {
            if (!SetProperty(ref _selectedProfileQueueItemV140, value)) return;
            OnPropertyChanged(nameof(SelectedProfileQueueReason));
            OpenProfileWorkQueueItemCommand.RaiseCanExecuteChanged();
            OpenNextProfileWorkQueueItemCommand.RaiseCanExecuteChanged();
        }
    }

    public string ProfileQueueSummary
    {
        get => _profileQueueSummaryV140;
        private set => SetProperty(ref _profileQueueSummaryV140, value);
    }

    public string ProfileQueueDetail
    {
        get => _profileQueueDetailV140;
        private set => SetProperty(ref _profileQueueDetailV140, value);
    }

    public string ProfileQueueDisplayed
    {
        get => _profileQueueDisplayedV140;
        private set => SetProperty(ref _profileQueueDisplayedV140, value);
    }

    public string SelectedProfileQueueReason => SelectedProfileQueueItem is null
        ? "Zaznacz pozycję kolejki, aby zobaczyć następny krok."
        : $"{SelectedProfileQueueItem.PrimaryCategory} • {SelectedProfileQueueItem.MissingFlags}\n{SelectedProfileQueueItem.Reason}";

    private void InitializeProfileWorkQueueV140(ApplicationDataPaths paths)
    {
        _profileWorkQueue = new ProfileWorkQueueService(paths.ActiveCatalogPath);
        RefreshProfileWorkQueueCommand = new AsyncRelayCommand(RefreshProfileWorkQueueCommandV140Async, () => !IsBusy);
        OpenProfileWorkQueueItemCommand = new AsyncRelayCommand(OpenSelectedProfileWorkQueueItemV140Async,
            () => !IsBusy && SelectedProfileQueueItem is not null);
        OpenNextProfileWorkQueueItemCommand = new AsyncRelayCommand(OpenNextProfileWorkQueueItemV140Async,
            () => !IsBusy && ProfileWorkQueue.Count > 0);
    }

    private async Task InitializeProfileWorkQueueAsyncV140()
    {
        await _profileWorkQueue.InitializeAsync();
        _profileWorkQueueInitializedV140 = true;
        await RefreshProfileWorkQueueV140Async(rebuildAll: true);
    }

    private async Task RefreshProfileWorkQueueCommandV140Async()
    {
        try
        {
            IsBusy = true;
            await RefreshProfileWorkQueueV140Async(rebuildAll: true);
            StatusMessage = "Kolejka BLOCKED/PENDING została przeliczona bez automatycznej zmiany statusów READY.";
        }
        catch (Exception ex)
        {
            StatusMessage = "Błąd odświeżania kolejki profili: " + ex.Message;
            await LogAsync(StatusMessage);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RefreshProfileWorkQueueV140Async(bool rebuildAll, string? profileId = null)
    {
        if (!_profileWorkQueueInitializedV140) return;
        ProfileWorkQueueSummary summary;
        if (!string.IsNullOrWhiteSpace(profileId))
            summary = await _profileWorkQueue.RefreshProfileAsync(profileId, SelectedHardware);
        else if (rebuildAll)
            summary = await _profileWorkQueue.RefreshAsync(SelectedHardware);
        else
            summary = await _profileWorkQueue.GetSummaryAsync(SelectedHardware.DatabaseId);

        ProfileQueueSummary = summary.Headline;
        ProfileQueueDetail = summary.Detail;
        await LoadProfileWorkQueueListV140Async();
    }

    private async Task LoadProfileWorkQueueListSafeV140Async()
    {
        if (!_profileWorkQueueInitializedV140) return;
        try { await LoadProfileWorkQueueListV140Async(); }
        catch (Exception ex) { StatusMessage = "Nie udało się przefiltrować kolejki: " + ex.Message; }
    }

    private async Task LoadProfileWorkQueueListV140Async()
    {
        var selectedId = SelectedProfileQueueItem?.ProfileId;
        var rows = await _profileWorkQueue.SearchAsync(
            SelectedHardware.DatabaseId,
            ProfileQueueCategory,
            ProfileQueueSearch,
            1500);
        ProfileWorkQueue.Clear();
        foreach (var item in rows) ProfileWorkQueue.Add(item);
        SelectedProfileQueueItem = !string.IsNullOrWhiteSpace(selectedId)
            ? ProfileWorkQueue.FirstOrDefault(x => x.ProfileId.Equals(selectedId, StringComparison.OrdinalIgnoreCase))
            : ProfileWorkQueue.FirstOrDefault();
        ProfileQueueDisplayed = $"{ProfileWorkQueue.Count:N0} wyświetlonych • limit widoku 1500";
        OpenNextProfileWorkQueueItemCommand.RaiseCanExecuteChanged();
    }

    private async Task OpenSelectedProfileWorkQueueItemV140Async()
    {
        if (SelectedProfileQueueItem is null) return;
        try
        {
            IsBusy = true;
            var draft = await _databaseProfileEditor.LoadAsync(SelectedProfileQueueItem.ProfileId);
            DatabaseProfileEditor.Load(draft);
            SelectedTab = 5;
            DatabaseProfileEditor.Status =
                $"Kolejka: {SelectedProfileQueueItem.PrimaryCategory}. Braki: {SelectedProfileQueueItem.MissingFlags}. " +
                "Szkic nie zmienia READY bez jawnego zatwierdzenia.";
            StatusMessage = $"Otworzono {SelectedProfileQueueItem.ProfileId} z kolejki do ręcznej weryfikacji.";
        }
        catch (Exception ex)
        {
            StatusMessage = "Nie udało się otworzyć pozycji kolejki: " + ex.Message;
            await LogAsync(StatusMessage);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task OpenNextProfileWorkQueueItemV140Async()
    {
        if (ProfileWorkQueue.Count == 0) return;
        var current = SelectedProfileQueueItem is null ? -1 : ProfileWorkQueue.IndexOf(SelectedProfileQueueItem);
        var next = current < 0 || current + 1 >= ProfileWorkQueue.Count ? 0 : current + 1;
        SelectedProfileQueueItem = ProfileWorkQueue[next];
        await OpenSelectedProfileWorkQueueItemV140Async();
    }

    private void OnHardwareChangedV140()
    {
        if (_profileWorkQueueInitializedV140)
            _ = RefreshProfileWorkQueueOnHardwareChangeSafeV140Async();
    }

    private async Task RefreshProfileWorkQueueOnHardwareChangeSafeV140Async()
    {
        try { await RefreshProfileWorkQueueV140Async(rebuildAll: true); }
        catch (Exception ex) { StatusMessage = "Kolejka dla nowego wariantu sprzętu: " + ex.Message; }
    }

    private async Task RefreshProfileWorkQueueAfterEditV140Async(string profileId)
    {
        if (!_profileWorkQueueInitializedV140) return;
        try { await RefreshProfileWorkQueueV140Async(rebuildAll: false, profileId); }
        catch (Exception ex) { StatusMessage += " • kolejka: " + ex.Message; }
    }

    private void RaiseProfileWorkQueueCommandStatesV140()
    {
        RefreshProfileWorkQueueCommand.RaiseCanExecuteChanged();
        OpenProfileWorkQueueItemCommand.RaiseCanExecuteChanged();
        OpenNextProfileWorkQueueItemCommand.RaiseCanExecuteChanged();
    }
}
