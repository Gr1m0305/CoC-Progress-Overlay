using System.Collections.ObjectModel;
using System.Windows.Media;
using System.Windows.Threading;
using ClashOverlay.App.Services;
using ClashOverlay.Core.Calc;
using ClashOverlay.Core.Data;
using ClashOverlay.Core.Display;
using ClashOverlay.Core.Settings;
using ClashOverlay.Core.State;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace ClashOverlay.App.ViewModels;

/// <summary>One entry of the account switcher.</summary>
internal sealed partial class AccountItemViewModel(AccountSettings account, int index) : ObservableObject
{
    public AccountSettings Account { get; } = account;

    public string Tag => Account.Tag;

    public int Index { get; } = index;

    public string Name => Account.Nickname is { Length: > 0 } n ? n : Account.Name ?? Account.Tag;

    public string Detail => Account.Nickname is { Length: > 0 } && Account.Name is { } real ? $"{real} · {Account.Tag}" : Account.Tag;

    public string Hall => Account.TownHall > 0 ? $"TH{Account.TownHall}" : "";

    public string KeyHint => Index < 9 ? $"Ctrl+{Index + 1}" : "";

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private ImageSource? _hallImage;
}

/// <summary>
/// The overlay's state: which account and village are shown, the header line, and the tabs. Data is refreshed when
/// the overlay opens (the API player if older than 5 minutes) and every 2 minutes while it is open; nothing runs while
/// it is hidden.
/// </summary>
internal sealed partial class OverlayViewModel : ObservableObject
{
    public static readonly TimeSpan RefreshWhileOpen = TimeSpan.FromMinutes(2);

    private readonly AppHost _host;
    private readonly ILogger _log;
    private readonly DispatcherTimer _refresh;
    private readonly DispatcherTimer _clock;
    private readonly Dictionary<string, int> _selection = new();
    private CancellationTokenSource? _loading;
    private (string Tag, ProfileData Data)? _profile;

    public OverlayViewModel(AppHost host)
    {
        _host = host;
        _log = host.Loggers.CreateLogger("Overlay");
        _village = host.Settings.SelectedVillage;
        _refresh = new DispatcherTimer { Interval = RefreshWhileOpen };
        _refresh.Tick += async (_, _) => await LoadAsync(force: false);
        _clock = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _clock.Tick += (_, _) => Tick();
        host.SettingsChanged += (_, _) => OnSettingsChanged();
        host.Data.Changed += async (_, _) => await LoadAsync(force: false);
        RebuildAccounts();
    }

    public AppHost Host => _host;

    public ObservableCollection<AccountItemViewModel> Accounts { get; } = [];

    public ObservableCollection<TabViewModel> Tabs { get; } = [];

    public event EventHandler? CloseRequested;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBuilderBase), nameof(AccentBrush), nameof(AccentSoftBrush), nameof(VillageName))]
    private Village _village;

    [ObservableProperty]
    private AccountItemViewModel? _selectedAccount;

    [ObservableProperty]
    private AccountData? _data;

    [ObservableProperty]
    private TabViewModel? _selectedTab;

    [ObservableProperty]
    private bool _isAccountMenuOpen;

    [ObservableProperty]
    private bool _isLoading;

    /// <summary>A problem worth showing above the tabs (no data, no export, key refused).</summary>
    [ObservableProperty]
    private string? _notice;

    [ObservableProperty]
    private string _syncText = "";

    [ObservableProperty]
    private bool _syncWarning;

    [ObservableProperty]
    private string? _syncTooltip;

    [ObservableProperty]
    private string _hotkeyHints = "";

    public bool IsBuilderBase => Village == Village.BuilderBase;

    public string VillageName => Village == Village.Home ? "Main Village" : "Builder Base";

    public Brush AccentBrush => IsBuilderBase ? Palette.Blue : Palette.Gold;

    public Brush AccentSoftBrush => IsBuilderBase ? Palette.BlueSoft : Palette.GoldSoft;

    /// <summary>The glass at the opacity chosen in settings (about 86% by default).</summary>
    public Brush PanelBrush => Palette.Glass(_host.Settings.PanelOpacity);

    public bool IsDemo => _host.IsDemo;

    public string AccountName => Data?.Name ?? SelectedAccount?.Name ?? "No account";

    public string AccountTag => SelectedAccount?.Tag ?? "";

    public string AccountHall
    {
        get
        {
            var th = Data?.Player?.Value?.TownHallLevel ?? SelectedAccount?.Account.TownHall ?? 0;
            var bh = Data?.Player?.Value?.BuilderHallLevel ?? 0;
            return IsBuilderBase ? bh > 0 ? $"BH{bh}" : "" : th > 0 ? $"TH{th}" : "";
        }
    }

    public ImageSource? AccountImage
    {
        get
        {
            var game = _host.Data.Data;
            if (game is null)
            {
                return null;
            }

            var th = Data?.Player?.Value?.TownHallLevel ?? SelectedAccount?.Account.TownHall ?? 0;
            return th > 0 ? _host.Images.Item(game.Hall(Village.Home), th, 64) : null;
        }
    }

    public void OnShown()
    {
        _refresh.Start();
        _clock.Start();
        Tick();
        _ = LoadAsync(force: false);
    }

    public void OnHidden()
    {
        _refresh.Stop();
        _clock.Stop();
        IsAccountMenuOpen = false;
    }

    public void SwitchVillage() => Village = Village == Village.Home ? Village.BuilderBase : Village.Home;

    public void NextAccount()
    {
        if (Accounts.Count > 1 && SelectedAccount is { } current)
        {
            SelectAccount((Accounts.IndexOf(current) + 1) % Accounts.Count);
        }
    }

    public void SelectAccount(int index)
    {
        if (index >= 0 && index < Accounts.Count)
        {
            SelectedAccount = Accounts[index];
        }
    }

    [RelayCommand]
    private void Close() => CloseRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void ShowVillage(Village village) => Village = village;

    [RelayCommand]
    private void ToggleAccountMenu() => IsAccountMenuOpen = !IsAccountMenuOpen && Accounts.Count > 0;

    [RelayCommand]
    private void PickAccount(AccountItemViewModel? item)
    {
        IsAccountMenuOpen = false;
        if (item is not null)
        {
            SelectedAccount = item;
        }
    }

    [RelayCommand]
    private Task Refresh() => LoadAsync(force: true);

    /// <summary>"+1": marks <paramref name="count"/> copies of <paramref name="item"/> at <paramref name="fromLevel"/> as upgraded.</summary>
    public async Task RaiseAsync(ItemDef item, int fromLevel, int count)
    {
        if (SelectedAccount is not { } account || LevelEdits.Raise(item, fromLevel, count, _host.Time.GetUtcNow()) is not { } edit)
        {
            return;
        }

        var edits = _host.Store.LoadEdits(account.Tag).Append(edit).ToList();
        _host.Store.SaveEdits(account.Tag, edits);
        _log.LogInformation("Hand edit for {Tag}: {Item} {From} -> {To} x{Count}", account.Tag, item.Name, edit.FromLevel, edit.ToLevel, edit.Count);
        await LoadAsync(force: false);
    }

    /// <summary>Takes back the latest hand edit of <paramref name="item"/>.</summary>
    public async Task UndoAsync(ItemDef item)
    {
        if (SelectedAccount is not { } account)
        {
            return;
        }

        _host.Store.SaveEdits(account.Tag, LevelEdits.UndoLast(_host.Store.LoadEdits(account.Tag), item.Id));
        await LoadAsync(force: false);
    }

    /// <summary>
    /// Takes back the latest hand edit in the shown village. Reaches items an edit took to max level, whose rows are
    /// gone from the upgrade lists.
    /// </summary>
    public async Task UndoLatestAsync()
    {
        if (SelectedAccount is not { } account || _host.Data.Data is not { } game)
        {
            return;
        }

        var edits = _host.Store.LoadEdits(account.Tag);
        if (edits.Where(e => game.Get(e.ItemId)?.Village == Village).MaxBy(e => e.At) is { } last)
        {
            _host.Store.SaveEdits(account.Tag, LevelEdits.UndoLast(edits, last.ItemId));
            await LoadAsync(force: false);
        }
    }

    public void SetProgressMode(ProgressMode mode)
    {
        if (_host.Settings.ProgressMode != mode)
        {
            _host.Settings.ProgressMode = mode;
            _host.SaveSettings();
        }
    }

    /// <summary>The row last selected in a tab, kept across reloads (e.g. after "+1").</summary>
    public int RememberedSelection(string tabKey) => _selection.GetValueOrDefault($"{Village}/{tabKey}", -1);

    public void RememberSelection(string tabKey, int itemId) => _selection[$"{Village}/{tabKey}"] = itemId;

    partial void OnVillageChanged(Village value)
    {
        _host.Settings.SelectedVillage = value;
        if (!_host.IsDemo)
        {
            _host.SaveSettings();
        }

        RebuildTabs();
        Tick();
        OnPropertyChanged(nameof(AccountHall));
    }

    partial void OnSelectedAccountChanged(AccountItemViewModel? oldValue, AccountItemViewModel? newValue)
    {
        if (oldValue is not null)
        {
            oldValue.IsSelected = false;
        }

        if (newValue is null)
        {
            return;
        }

        newValue.IsSelected = true;
        if (_host.Settings.SelectedAccount != newValue.Tag)
        {
            _host.Settings.SelectedAccount = newValue.Tag;
            _host.SaveSettings();
        }

        Data = null;
        OnPropertyChanged(nameof(AccountName));
        OnPropertyChanged(nameof(AccountTag));
        _ = LoadAsync(force: false);
    }

    partial void OnDataChanged(AccountData? value)
    {
        OnPropertyChanged(nameof(AccountName));
        OnPropertyChanged(nameof(AccountHall));
        OnPropertyChanged(nameof(AccountImage));
        RebuildTabs();
        Tick();
    }

    private void OnSettingsChanged()
    {
        var selected = SelectedAccount?.Tag;
        if (!Accounts.Select(a => a.Tag).SequenceEqual(_host.Settings.Accounts.Select(a => a.Tag))
            || Accounts.Zip(_host.Settings.Accounts).Any(p => p.First.Name != (p.Second.Nickname is { Length: > 0 } n ? n : p.Second.Name ?? p.Second.Tag)))
        {
            RebuildAccounts();
        }

        UpdateHotkeyHints();
        OnPropertyChanged(nameof(PanelBrush));
        if (selected == SelectedAccount?.Tag)
        {
            _ = LoadAsync(force: false); // progress mode or builders may have changed
        }
    }

    private void RebuildAccounts()
    {
        var keep = _host.Settings.SelectedAccount;
        Accounts.Clear();
        var i = 0;
        foreach (var a in _host.Settings.Accounts)
        {
            Accounts.Add(new AccountItemViewModel(a, i++) { HallImage = HallImage(a.TownHall) });
        }

        UpdateHotkeyHints();
        SelectedAccount = Accounts.FirstOrDefault(a => a.Tag == keep) ?? Accounts.FirstOrDefault();
        if (SelectedAccount is null)
        {
            Data = null;
        }
    }

    private ImageSource? HallImage(int th) =>
        th > 0 && _host.Data.Data is { } game ? _host.Images.Item(game.Hall(Village.Home), th, 64) : null;

    private void UpdateHotkeyHints()
    {
        var k = _host.Settings.Hotkeys;
        var parts = new List<string> { $"{k.Toggle} hide", $"{k.SwitchVillage} village" };
        if (_host.Settings.Accounts.Count > 1)
        {
            parts.Add($"{k.NextAccount} account");
        }

        HotkeyHints = string.Join("  ·  ", parts);
    }

    /// <summary>Loads the selected account (cache first; the API only when stale or forced).</summary>
    public async Task LoadAsync(bool force)
    {
        if (SelectedAccount is not { } account)
        {
            Notice = _host.IsDemo ? null : "Add an account in Settings to get started.";
            return;
        }

        _loading?.Cancel();
        var cts = _loading = new CancellationTokenSource();
        IsLoading = true;
        try
        {
            if (_host.Data.Data is null && !_host.Data.TryLoad())
            {
                Notice = "Downloading game data…";
            }

            var data = await _host.Loader.LoadAsync(account.Account, force, cts.Token);
            if (cts.IsCancellationRequested)
            {
                return;
            }

            RememberHall(account.Account, data);
            Data = data;
            Notice = NoticeFor(data);
            await LoadProfileAsync(data, force, cts.Token);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception e)
        {
            _log.LogError(e, "Loading account {Tag} failed", account.Tag);
            Notice = "Something went wrong loading this account. See the log for details.";
        }
        finally
        {
            if (_loading == cts)
            {
                IsLoading = false;
            }
        }
    }

    /// <summary>Profile & War data, loaded after the village so the main tabs appear first.</summary>
    private async Task LoadProfileAsync(AccountData data, bool force, CancellationToken ct)
    {
        if (data.Player?.Value is not { } player)
        {
            return;
        }

        var profile = await _host.Loader.LoadProfileAsync(player, force, ct);
        if (!ct.IsCancellationRequested && Data == data)
        {
            _profile = (data.Account.Tag, profile);
            foreach (var tab in Tabs)
            {
                tab.OnProfile(profile);
            }
        }
    }

    private string? NoticeFor(AccountData data)
    {
        if (_host.Data.Data is null)
        {
            return _host.Data.Problem ?? "Game data is not available yet.";
        }

        if (data.Player?.Error is { } e && data.Player.Value is null)
        {
            return e.IsKeyProblem ? $"API: {e.Message}" : $"API unavailable: {e.Message}";
        }

        if (data.ExportProblem is { } p)
        {
            return $"The saved export could not be read: {p}";
        }

        return null;
    }

    /// <summary>Remembers the Town Hall level and when it changed, for the export warning and the switcher.</summary>
    private void RememberHall(AccountSettings account, AccountData data)
    {
        if (data.Player?.Value is not { } p || _host.IsDemo)
        {
            return;
        }

        var changed = false;
        if (p.TownHallLevel > 0 && p.TownHallLevel != account.TownHall)
        {
            if (account.TownHall > 0)
            {
                account.TownHallChangedAt = data.Player.FetchedAt ?? _host.Time.GetUtcNow();
            }

            account.TownHall = p.TownHallLevel;
            changed = true;
        }

        if (p.Name != account.Name)
        {
            account.Name = p.Name;
            changed = true;
        }

        if (changed)
        {
            _host.SaveSettings();
        }
    }

    private void Tick()
    {
        var now = _host.Time.GetUtcNow();
        var d = Data;
        var report = d?.Report(Village);
        var status = SyncStatus.Compute(now, d?.Player?.FetchedAt, d?.Player?.IsStale == true, d?.Export?.Timestamp,
            Village == Village.Home ? d?.ExportHall ?? 0 : 0,
            Village == Village.Home ? d?.Player?.Value?.TownHallLevel ?? 0 : 0,
            SelectedAccount?.Account.TownHallChangedAt);
        SyncText = status.Text;
        SyncWarning = status.ExportWarning;
        SyncTooltip = status.WarningReason ?? (report is null ? null : $"Data as of {report.State.AsOf.ToLocalTime():g}");
        foreach (var tab in Tabs)
        {
            tab.Tick(now);
        }
    }

    private void RebuildTabs()
    {
        var keep = SelectedTab?.Key;
        Tabs.Clear();
        foreach (var tab in TabFactory.Create(this, Data, Village))
        {
            // Clan and war data from the last load, until the next one arrives.
            if (_profile is { } p && p.Tag == Data?.Account.Tag)
            {
                tab.OnProfile(p.Data);
            }

            Tabs.Add(tab);
        }

        SelectedTab = Tabs.FirstOrDefault(t => t.Key == keep) ?? Tabs.FirstOrDefault();
    }
}
