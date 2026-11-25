using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Threading;
using ClashOverlay.Api;
using ClashOverlay.App.Native;
using ClashOverlay.App.Overlay;
using ClashOverlay.App.Services;
using ClashOverlay.Core.Calc;
using ClashOverlay.Core.Display;
using ClashOverlay.Core.Settings;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ClashOverlay.App.ViewModels;

/// <summary>What the settings window asks the app to do.</summary>
internal sealed class SettingsActions
{
    /// <summary>Opens first-run setup at a step (1 = make a key, 2 = paste it or save the developer login).</summary>
    public required Action<int> OpenSetup { get; init; }

    /// <summary>Opens the import window for an account.</summary>
    public required Action<string?> OpenImport { get; init; }

    /// <summary>Asks the user: title, question. True for yes.</summary>
    public required Func<string, string, bool> Confirm { get; init; }

    public required Action<string> OpenFolder { get; init; }
}

internal sealed record SettingsSection(string Key, string Title);

internal sealed record ModifierChoice(string Value, string Label);

/// <summary>
/// The settings window. Every change is applied at once (the overlay, hotkeys and detector listen to
/// <see cref="AppHost.SettingsChanged"/>) and saved after a short pause, so dragging a slider writes once.
/// </summary>
internal sealed partial class SettingsViewModel : ObservableObject, IDisposable
{
    public const string OverlayPage = "overlay";
    public const string HotkeysPage = "hotkeys";
    public const string AccountsPage = "accounts";
    public const string KeyPage = "key";
    public const string DetectionPage = "detection";
    public const string DataPage = "data";

    private readonly AppHost _host;
    private readonly GameDetector? _detector;
    private readonly SettingsActions _actions;
    private readonly DispatcherTimer _saveTimer;
    private HotkeySettings _keys;
    private bool _rescan;

    public SettingsViewModel(AppHost host, GameDetector? detector, SettingsActions actions, string? page = null)
    {
        _host = host;
        _detector = detector;
        _actions = actions;
        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        _saveTimer.Tick += (_, _) => SaveNow();
        _keys = CopyKeys(host.Settings.Hotkeys);
        _section = Sections.FirstOrDefault(s => s.Key == page) ?? Sections[0];

        Lookup = new AccountLookupViewModel(host);
        Lookup.Added += (_, _) => RebuildAccounts();
        RebuildAccounts();
        RebuildRules();

        host.Store.Changed += OnStoreChanged;
        host.Keys.Changed += OnKeyChanged;
        host.Content.StatusChanged += OnContentStatus;
        if (detector is not null)
        {
            detector.Changed += OnDetectorChanged;
        }

        host.Updates.Changed += OnUpdatesChanged;
        _appUpdateStatus = AppUpdateSummary();
        _ = FindIpAsync();
    }

    private AppSettings S => _host.Settings;

    public bool IsDemo => _host.IsDemo;

    public bool CanChangeSystem => !_host.IsDemo;

    public IReadOnlyList<SettingsSection> Sections { get; } =
    [
        new(OverlayPage, "Overlay"),
        new(HotkeysPage, "Hotkeys"),
        new(AccountsPage, "Accounts"),
        new(KeyPage, "API key"),
        new(DetectionPage, "Game detection"),
        new(DataPage, "Data and updates"),
    ];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOverlayPage), nameof(IsHotkeysPage), nameof(IsAccountsPage), nameof(IsKeyPage), nameof(IsDetectionPage), nameof(IsDataPage))]
    private SettingsSection _section;

    public bool IsOverlayPage => Section.Key == OverlayPage;

    public bool IsHotkeysPage => Section.Key == HotkeysPage;

    public bool IsAccountsPage => Section.Key == AccountsPage;

    public bool IsKeyPage => Section.Key == KeyPage;

    public bool IsDetectionPage => Section.Key == DetectionPage;

    public bool IsDataPage => Section.Key == DataPage;

    public void ShowPage(string? key)
    {
        if (Sections.FirstOrDefault(s => s.Key == key) is { } s)
        {
            Section = s;
        }
    }

    partial void OnSectionChanged(SettingsSection value)
    {
        if (value.Key == KeyPage)
        {
            RefreshKey();
        }
        else if (value.Key == DataPage)
        {
            RefreshData();
        }
        else if (value.Key == AccountsPage)
        {
            foreach (var row in Accounts)
            {
                row.Refresh();
            }
        }
    }

    // ----- saving -----

    private void Raise(params string[] properties)
    {
        foreach (var p in properties)
        {
            OnPropertyChanged(p);
        }
    }

    /// <summary>Notifies the properties and saves after a short pause.</summary>
    private void Changed(params string[] properties)
    {
        foreach (var p in properties)
        {
            OnPropertyChanged(p);
        }

        _saveTimer.Stop();
        _saveTimer.Start();
    }

    public void SaveNow()
    {
        _saveTimer.Stop();
        _host.SaveSettings();
        if (_rescan)
        {
            _rescan = false;
            _detector?.Rescan();
        }
    }

    /// <summary>Saves anything still waiting (window closing).</summary>
    public void Flush()
    {
        if (_saveTimer.IsEnabled)
        {
            SaveNow();
        }
    }

    // ----- overlay -----

    public bool PlaceOnGameMonitor
    {
        get => S.Placement == OverlayPlacement.CenterOnGameMonitor;
        set
        {
            if (value)
            {
                S.Placement = OverlayPlacement.CenterOnGameMonitor;
                S.OverlayLeft = S.OverlayTop = null;
                Changed(nameof(PlaceOnGameMonitor), nameof(PlaceOverGame), nameof(HasCustomPosition));
            }
        }
    }

    public bool PlaceOverGame
    {
        get => S.Placement == OverlayPlacement.OverGameWindow;
        set
        {
            if (value)
            {
                S.Placement = OverlayPlacement.OverGameWindow;
                S.OverlayLeft = S.OverlayTop = null;
                Changed(nameof(PlaceOnGameMonitor), nameof(PlaceOverGame), nameof(HasCustomPosition));
            }
        }
    }

    public double Scale
    {
        get => S.Scale;
        set
        {
            S.Scale = Math.Clamp(Math.Round(value / 0.05) * 0.05, AppSettings.MinScale, AppSettings.MaxScale);
            Changed(nameof(Scale), nameof(ScaleText), nameof(HasCustomPosition));
        }
    }

    public string ScaleText => Percent(S.Scale);

    public double MinScale => AppSettings.MinScale;

    public double MaxScale => AppSettings.MaxScale;

    /// <summary>The overlay was dragged somewhere or resized by hand.</summary>
    public bool HasCustomPosition => S.OverlayLeft is not null || Math.Abs(S.Scale - 1.0) > 0.001;

    /// <summary>Back to the chosen placement at 100%.</summary>
    [RelayCommand]
    private void ResetOverlayPosition()
    {
        S.OverlayLeft = null;
        S.OverlayTop = null;
        S.Scale = 1.0;
        Changed(nameof(Scale), nameof(ScaleText), nameof(HasCustomPosition));
    }

    /// <summary>Called when the overlay was dragged or resized while this window is open.</summary>
    public void OverlayMoved() => Raise(nameof(Scale), nameof(ScaleText), nameof(HasCustomPosition));

    public double PanelOpacity
    {
        get => S.PanelOpacity;
        set
        {
            S.PanelOpacity = Math.Clamp(Math.Round(value / 0.02) * 0.02, 0.5, 1.0);
            Changed(nameof(PanelOpacity), nameof(OpacityText));
        }
    }

    public string OpacityText => Percent(S.PanelOpacity);

    public bool HideWhenGameMinimised
    {
        get => S.HideWhenGameMinimised;
        set
        {
            S.HideWhenGameMinimised = value;
            Changed(nameof(HideWhenGameMinimised));
        }
    }

    public bool ProgressByLevels
    {
        get => S.ProgressMode == ProgressMode.Levels;
        set
        {
            if (value)
            {
                S.ProgressMode = ProgressMode.Levels;
                Changed(nameof(ProgressByLevels), nameof(ProgressByTime));
            }
        }
    }

    public bool ProgressByTime
    {
        get => S.ProgressMode == ProgressMode.Time;
        set
        {
            if (value)
            {
                S.ProgressMode = ProgressMode.Time;
                Changed(nameof(ProgressByLevels), nameof(ProgressByTime));
            }
        }
    }

    public bool NumbersCompact
    {
        get => S.NumberFormat == NumberFormat.Compact;
        set
        {
            if (value)
            {
                S.NumberFormat = NumberFormat.Compact;
                Changed(nameof(NumbersCompact), nameof(NumbersFull));
            }
        }
    }

    public bool NumbersFull
    {
        get => S.NumberFormat == NumberFormat.Full;
        set
        {
            if (value)
            {
                S.NumberFormat = NumberFormat.Full;
                Changed(nameof(NumbersCompact), nameof(NumbersFull));
            }
        }
    }

    public string CompactExample => $"{Fmt.Number(12_345_678, NumberFormat.Compact)}  ·  {Fmt.Number(456_789, NumberFormat.Compact)}";

    public string FullExample => $"{Fmt.Number(12_345_678, NumberFormat.Full)}  ·  {Fmt.Number(456_789, NumberFormat.Full)}";

    private int _builders = 6;

    public bool BuildersFromGame
    {
        get => S.BuildersOverride is null;
        set
        {
            S.BuildersOverride = value ? null : _builders;
            Changed(nameof(BuildersFromGame), nameof(BuildersFixed));
        }
    }

    public bool BuildersFixed
    {
        get => S.BuildersOverride is not null;
        set => BuildersFromGame = !value;
    }

    public IReadOnlyList<int> BuilderChoices { get; } = Enumerable.Range(1, 8).ToList();

    public int BuildersCount
    {
        get => S.BuildersOverride ?? _builders;
        set
        {
            _builders = Math.Clamp(value, 1, 12);
            if (S.BuildersOverride is not null)
            {
                S.BuildersOverride = _builders;
            }

            Changed(nameof(BuildersCount));
        }
    }

    private static string Percent(double v) => $"{Math.Round(v * 100).ToString(CultureInfo.InvariantCulture)}%";

    // ----- hotkeys -----

    public string ToggleKey
    {
        get => _keys.Toggle;
        set => EditKeys(k => k.Toggle = value, nameof(ToggleKey));
    }

    public string SwitchVillageKey
    {
        get => _keys.SwitchVillage;
        set => EditKeys(k => k.SwitchVillage = value, nameof(SwitchVillageKey));
    }

    public string NextAccountKey
    {
        get => _keys.NextAccount;
        set => EditKeys(k => k.NextAccount = value, nameof(NextAccountKey));
    }

    public string CloseKey
    {
        get => _keys.Close;
        set => EditKeys(k => k.Close = value, nameof(CloseKey));
    }

    public IReadOnlyList<ModifierChoice> ModifierChoices { get; } =
    [
        new("Ctrl", "Ctrl+1…9"),
        new("Alt", "Alt+1…9"),
        new("Ctrl+Shift", "Ctrl+Shift+1…9"),
        new("", "1…9 alone"),
    ];

    public ModifierChoice AccountModifier
    {
        get => ModifierChoices.FirstOrDefault(c => string.Equals(c.Value, _keys.AccountModifier, StringComparison.OrdinalIgnoreCase))
            ?? new ModifierChoice(_keys.AccountModifier, $"{_keys.AccountModifier}+1…9");
        set
        {
            if (value is not null)
            {
                EditKeys(k => k.AccountModifier = value.Value, nameof(AccountModifier));
            }
        }
    }

    [ObservableProperty]
    private string? _hotkeyProblems;

    private void EditKeys(Action<HotkeySettings> edit, string property)
    {
        edit(_keys);
        OnPropertyChanged(property);
        var problems = HotkeyPlan.Problems(_keys);
        HotkeyProblems = problems.Count == 0 ? null : string.Join("\n", problems) + "\nThe last working keys stay in use until this is fixed.";
        if (problems.Count == 0)
        {
            S.Hotkeys = CopyKeys(_keys);
            Changed();
        }
    }

    [RelayCommand]
    private void ResetHotkeys()
    {
        _keys = new HotkeySettings();
        EditKeys(_ => { }, nameof(ToggleKey));
        OnPropertyChanged(nameof(SwitchVillageKey));
        OnPropertyChanged(nameof(NextAccountKey));
        OnPropertyChanged(nameof(CloseKey));
        OnPropertyChanged(nameof(AccountModifier));
    }

    private static HotkeySettings CopyKeys(HotkeySettings k) => new()
    {
        Toggle = k.Toggle,
        SwitchVillage = k.SwitchVillage,
        NextAccount = k.NextAccount,
        AccountModifier = k.AccountModifier,
        Close = k.Close,
    };

    // ----- accounts -----

    public AccountLookupViewModel Lookup { get; }

    public ObservableCollection<AccountRowViewModel> Accounts { get; } = [];

    private void RebuildAccounts()
    {
        Accounts.Clear();
        var i = 1;
        foreach (var a in S.Accounts)
        {
            Accounts.Add(new AccountRowViewModel(this, a, i++));
        }
    }

    private void OnStoreChanged(object? sender, string tag) =>
        System.Windows.Application.Current.Dispatcher.BeginInvoke(() => Accounts.FirstOrDefault(a => a.Tag == tag)?.Refresh());

    internal AppHost Host => _host;

    internal void NicknameChanged() => Changed();

    internal void Import(AccountRowViewModel row) => _actions.OpenImport(row.Tag);

    internal void ClearEdits(AccountRowViewModel row)
    {
        if (_actions.Confirm("Clear manual changes", $"Forget the {row.EditCount} level(s) you raised by hand for {row.Name}? The overlay goes back to the last export and sync."))
        {
            _host.Store.ClearEdits(row.Tag);
            row.Refresh();
        }
    }

    internal void Remove(AccountRowViewModel row)
    {
        if (!_actions.Confirm("Remove account", $"Remove {row.Name} ({row.Tag})? Its saved export, hand edits and cached API data are deleted from this PC."))
        {
            return;
        }

        S.Accounts.RemoveAll(a => a.Tag == row.Tag);
        _host.Store.Delete(row.Tag);
        SaveNow();
        RebuildAccounts();
    }

    internal void Move(AccountRowViewModel row, int by)
    {
        var i = S.Accounts.FindIndex(a => a.Tag == row.Tag);
        var j = i + by;
        if (i < 0 || j < 0 || j >= S.Accounts.Count)
        {
            return;
        }

        (S.Accounts[i], S.Accounts[j]) = (S.Accounts[j], S.Accounts[i]);
        SaveNow();
        RebuildAccounts();
    }

    // ----- API key -----

    [ObservableProperty]
    private string _keyStatus = "";

    [ObservableProperty]
    private string? _keyAddresses;

    [ObservableProperty]
    private string? _ipStatus;

    [ObservableProperty]
    private bool _ipAllowed = true;

    [ObservableProperty]
    private bool _hasKey;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RetestKeyCommand))]
    private bool _isTestingKey;

    [ObservableProperty]
    private string? _keyTestResult;

    [ObservableProperty]
    private bool _keyTestOk;

    private string? _publicIp;

    private async Task FindIpAsync()
    {
        _publicIp = _host.IsDemo ? "203.0.113.24" : await _host.PublicIp.GetAsync();
        RefreshKey();
    }

    private void OnKeyChanged(object? sender, EventArgs e) => System.Windows.Application.Current.Dispatcher.BeginInvoke(RefreshKey);

    private void RefreshKey()
    {
        var key = _host.IsDemo ? null : _host.Keys.Load();
        HasKey = key is not null;
        if (_host.IsDemo)
        {
            KeyStatus = "Demo mode: no key is used.";
            KeyAddresses = null;
            IpStatus = null;
            IpAllowed = true;
        }
        else if (key is null)
        {
            KeyStatus = "No key saved.";
            KeyAddresses = null;
            IpStatus = _publicIp is null ? null : $"This PC's public IP address is {_publicIp}.";
            IpAllowed = true;
        }
        else
        {
            KeyStatus = $"Saved key ••••••••{key[^Math.Min(8, key.Length)..]}";
            var info = ApiKeyInfo.TryParse(key);
            KeyAddresses = info is null ? null : $"Allowed addresses: {string.Join(", ", info.AllowedIps)}";
            IpAllowed = info is null || _publicIp is null || info.Allows(_publicIp);
            IpStatus = _publicIp is null
                ? "This PC's public IP address could not be found."
                : IpAllowed ? $"This PC ({_publicIp}) is allowed." : $"This PC is now at {_publicIp}, which this key does not allow. Make a new key for it.";
        }

        OnPropertyChanged(nameof(LoginStatus));
        RetestKeyCommand.NotifyCanExecuteChanged();
    }

    private bool CanRetest() => HasKey && !IsTestingKey;

    [RelayCommand(CanExecute = nameof(CanRetest))]
    private async Task RetestKey()
    {
        if (_host.Keys.Load() is not { } key)
        {
            return;
        }

        IsTestingKey = true;
        KeyTestResult = null;
        try
        {
            var problem = await _host.Api.CheckKeyAsync(key);
            KeyTestOk = problem is null;
            KeyTestResult = problem is null ? "The key works." : problem.Message;
            if (problem is null)
            {
                _host.Api.ResetBackoff();
            }
        }
        finally
        {
            IsTestingKey = false;
        }
    }

    [RelayCommand]
    private void ReplaceKey() => _actions.OpenSetup(1);

    [RelayCommand]
    private void RemoveKey()
    {
        if (_actions.Confirm("Remove API key", "Delete the saved API key from this PC? The overlay shows only your last synced data until you add a new one."))
        {
            _host.Keys.Delete();
            KeyTestResult = null;
        }
    }

    public bool AutoKey
    {
        get => S.DeveloperSiteAutoKey;
        set
        {
            S.DeveloperSiteAutoKey = value;
            if (!value)
            {
                _host.Logins.Delete();
            }

            Changed(nameof(AutoKey), nameof(LoginStatus));
        }
    }

    public string LoginStatus => _host.IsDemo
        ? "Not used in demo mode."
        : _host.Logins.Load() is { } l ? $"Login saved for {l.Email}." : "No login saved yet. Use \"Enter login\" to add it.";

    [RelayCommand]
    private void EnterLogin() => _actions.OpenSetup(2);

    // ----- game detection -----

    public ObservableCollection<RuleRowViewModel> Rules { get; } = [];

    public string DetectionStatus => _detector?.Game is { } g
        ? $"Found \"{g.Window.Title}\" ({g.Window.ProcessName}), matched by: {g.RuleName}. "
          + (_detector.IsGameForeground ? $"It is in front now, so {S.Hotkeys.Toggle} shows the overlay." : $"Click the game, then press {S.Hotkeys.Toggle} to show the overlay.")
        : _detector is null ? "Detection is off in demo mode." : "The game is not running, or no rule matches its window. Start the game, or use Pick game window.";

    [ObservableProperty]
    private string? _pickStatus;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PickWindowCommand))]
    private bool _isPicking;

    private void OnDetectorChanged(object? sender, EventArgs e) => OnPropertyChanged(nameof(DetectionStatus));

    private void RebuildRules()
    {
        Rules.Clear();
        foreach (var r in S.GameRules)
        {
            Rules.Add(new RuleRowViewModel(this, r));
        }
    }

    internal void RulesEdited()
    {
        _rescan = true;
        Changed();
    }

    [RelayCommand]
    private void AddRule()
    {
        S.GameRules.Add(new GameWindowRule { Name = "My game window", TitlePattern = "Clash of Clans" });
        RebuildRules();
        RulesEdited();
    }

    [RelayCommand]
    private void RemoveRule(RuleRowViewModel? row)
    {
        if (row is not null && S.GameRules.Remove(row.Rule))
        {
            RebuildRules();
            RulesEdited();
        }
    }

    [RelayCommand]
    private void ResetRules()
    {
        if (_actions.Confirm("Reset detection rules", "Replace your rules with the built-in ones for Google Play Games and the common emulators?"))
        {
            S.GameRules = GameWindowRule.Defaults();
            RebuildRules();
            RulesEdited();
        }
    }

    private bool CanPick() => !IsPicking && _detector is not null;

    /// <summary>Waits for the user to click another window, then uses it and adds a rule for it.</summary>
    [RelayCommand(CanExecute = nameof(CanPick))]
    private async Task PickWindow()
    {
        IsPicking = true;
        try
        {
            PickStatus = "Click the game window now (within 15 seconds)…";
            WindowInfo? picked = null;
            var until = DateTime.UtcNow.AddSeconds(15);
            while (DateTime.UtcNow < until)
            {
                await Task.Delay(200);
                var hwnd = NativeMethods.GetForegroundWindow();
                if (hwnd != IntPtr.Zero && !NativeWindows.IsOwnProcess(hwnd) && NativeWindows.Describe(hwnd) is { Title.Length: > 0 } w)
                {
                    picked = w;
                    break;
                }
            }

            if (picked is null)
            {
                PickStatus = "No other window was clicked. Press the button and click the game.";
                return;
            }

            var exe = picked.ProcessName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? picked.ProcessName[..^4] : picked.ProcessName;
            var pattern = GameWindowRule.PatternForPicked(picked.Title);
            S.GameRules.RemoveAll(r => string.Equals(r.ProcessName, exe, StringComparison.OrdinalIgnoreCase) && r.TitlePattern == pattern);
            var shown = pattern == GameWindowRule.GameTitlePattern ? "Clash of Clans" : picked.Title;
            var name = shown.Length > 40 ? shown[..40] + "…" : shown;
            S.GameRules.Insert(0, new GameWindowRule { Name = $"Picked: {name}", ProcessName = exe, TitlePattern = pattern });
            RebuildRules();
            SaveNow();
            _detector!.Pin(picked.Handle);
            PickStatus = $"Using \"{picked.Title}\" ({exe}). A rule for it was added at the top of the list.";
            OnPropertyChanged(nameof(DetectionStatus));
        }
        finally
        {
            IsPicking = false;
        }
    }

    // ----- data and updates -----

    [ObservableProperty]
    private string _dataStatus = "";

    [ObservableProperty]
    private string _imagesStatus = "";

    [ObservableProperty]
    private string? _updateStatus;

    [ObservableProperty]
    private bool _updateOk = true;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UpdateContentCommand))]
    private bool _isUpdating;

    private void RefreshData()
    {
        var now = _host.Time.GetUtcNow();
        DataStatus = _host.Data.Data is { } game
            ? $"Game data: {game.Source.SourceName}"
              + (game.Source.CommitDate is { } d ? $", published {d:yyyy-MM-dd}" : "")
              + (game.Source.DownloadedAt is { } at ? $", downloaded {Fmt.Ago(now - at)}" : "")
              + $". Covers up to Town Hall {game.MaxTownHall} and Builder Hall {game.MaxBuilderHall}."
            : _host.Data.Problem ?? "Game data has not been downloaded yet.";
        var manifest = ClashOverlay.AssetFetch.LocalAssetManifest.Read(_host.Paths.Assets);
        ImagesStatus = manifest.Files.Count == 0
            ? "Images have not been downloaded yet."
            : $"Images: {manifest.Files.Count.ToString("N0", CultureInfo.InvariantCulture)} files"
              + (manifest.UpdatedAt is { } u ? $", checked {Fmt.Ago(now - u)}." : ".");
        IsUpdating = _host.Content.IsRunning;
        if (IsUpdating)
        {
            UpdateStatus = _host.Content.Status;
        }
    }

    private void OnContentStatus(object? sender, EventArgs e) =>
        System.Windows.Application.Current.Dispatcher.BeginInvoke(() =>
        {
            if (_host.Content.Status is { } s)
            {
                UpdateOk = true;
                UpdateStatus = s;
            }

            IsUpdating = _host.Content.IsRunning;
        });

    private bool CanUpdate() => !IsUpdating && !_host.IsDemo;

    [RelayCommand(CanExecute = nameof(CanUpdate))]
    private async Task UpdateContent()
    {
        IsUpdating = true;
        try
        {
            var result = await _host.Content.RunAsync(force: false);
            UpdateOk = result.Ok;
            UpdateStatus = $"{result.Title}. {result.Detail}";
        }
        finally
        {
            IsUpdating = false;
            RefreshData();
        }
    }

    public bool InstallUpdatesAutomatically
    {
        get => S.InstallUpdatesAutomatically;
        set
        {
            S.InstallUpdatesAutomatically = value;
            Changed(nameof(InstallUpdatesAutomatically));
        }
    }

    /// <summary>A portable copy (or one built from source) is told about new versions but never replaced.</summary>
    public bool CanInstallUpdates => _host.Updates.CanInstall;

    public string UpdatesHint => _host.Updates.CanInstall
        ? "Clash Tracker checks for a new version when it starts (when you sign in to Windows) and when you press Check now. New versions install quietly and Clash Tracker starts again in the notification area. Turn this off to install them yourself from here or the tray menu."
        : "Clash Tracker checks for a new version when it starts and when you press Check now. This copy was not installed with Clash Tracker's setup, so it cannot update itself: download the new version from the release page.";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CheckAppUpdateCommand))]
    private bool _isCheckingApp;

    [ObservableProperty]
    private string? _appUpdateStatus;

    /// <summary>The release page of a newer version, when one is known.</summary>
    public string? AppUpdateUrl => _host.Updates.Available?.Url;

    /// <summary>A newer version is known and this copy can install it.</summary>
    public bool CanInstallAppUpdate => _host.Updates is { Available: not null, CanInstall: true, State: UpdateState.Idle };

    public string? InstallUpdateText => _host.Updates.Available is { } r ? $"Install {r.VersionText} now" : null;

    private string? AppUpdateSummary()
    {
        var u = _host.Updates;
        if (u.Available is { } r)
        {
            return u.State switch
            {
                UpdateState.Downloading => $"Downloading Clash Tracker {r.VersionText}…",
                UpdateState.Installing => $"Installing Clash Tracker {r.VersionText}. It starts again in a few seconds.",
                _ => u.Problem ?? $"Clash Tracker {r.VersionText} is out. You have {_host.Version}.",
            };
        }

        return S.LastUpdateCheck is { } at ? $"You have {_host.Version}. Last checked {Fmt.Ago(_host.Time.GetUtcNow() - at)}." : $"You have {_host.Version}.";
    }

    private void OnUpdatesChanged(object? sender, EventArgs e)
    {
        if (!IsCheckingApp)
        {
            AppUpdateStatus = AppUpdateSummary();
        }

        Raise(nameof(AppUpdateUrl), nameof(CanInstallAppUpdate), nameof(InstallUpdateText));
        CheckAppUpdateCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private async Task InstallAppUpdate()
    {
        IsCheckingApp = true;
        try
        {
            AppUpdateStatus = await _host.Updates.InstallAsync();
        }
        finally
        {
            IsCheckingApp = false;
            Raise(nameof(AppUpdateUrl), nameof(CanInstallAppUpdate), nameof(InstallUpdateText));
        }
    }

    private bool CanCheckApp() => !IsCheckingApp && _host.Updates is { CanCheck: true, State: UpdateState.Idle };

    [RelayCommand(CanExecute = nameof(CanCheckApp))]
    private async Task CheckAppUpdate()
    {
        IsCheckingApp = true;
        AppUpdateStatus = "Checking…";
        try
        {
            AppUpdateStatus = await _host.Updates.CheckNowAsync();
        }
        finally
        {
            IsCheckingApp = false;
            Raise(nameof(AppUpdateUrl), nameof(CanInstallAppUpdate), nameof(InstallUpdateText));
        }
    }

    public bool StartWithWindows
    {
        get => !_host.IsDemo && StartupRegistration.IsEnabled;
        set
        {
            if (_host.IsDemo)
            {
                return;
            }

            StartupRegistration.Set(value);
            OnPropertyChanged(nameof(StartWithWindows));
            OnPropertyChanged(nameof(StartupNote));
        }
    }

    public string? StartupNote => !_host.IsDemo && StartupRegistration.PointsElsewhere
        ? "Windows is set to start a copy of Clash Tracker from another folder. Turn this off and on again to use this copy."
        : null;

    [RelayCommand]
    private void OpenDataFolder() => _actions.OpenFolder(_host.Paths.Root);

    [RelayCommand]
    private void OpenLogs() => _actions.OpenFolder(_host.Paths.Logs);

    public string VersionText => $"Version {_host.Version}";

    [RelayCommand]
    private void OpenLink(string? url)
    {
        if (!string.IsNullOrEmpty(url))
        {
            Shell.Open(url);
        }
    }

    public void Dispose()
    {
        Flush();
        _host.Store.Changed -= OnStoreChanged;
        _host.Keys.Changed -= OnKeyChanged;
        _host.Content.StatusChanged -= OnContentStatus;
        _host.Updates.Changed -= OnUpdatesChanged;
        if (_detector is not null)
        {
            _detector.Changed -= OnDetectorChanged;
        }
    }
}

/// <summary>One account in settings: nickname, what is stored for it, and its buttons.</summary>
internal sealed partial class AccountRowViewModel : ObservableObject
{
    private readonly SettingsViewModel _owner;
    private readonly AccountSettings _account;

    public AccountRowViewModel(SettingsViewModel owner, AccountSettings account, int number)
    {
        _owner = owner;
        _account = account;
        Number = number;
        Refresh();
    }

    public int Number { get; }

    public string Tag => _account.Tag;

    public string Name => _account.Nickname is { Length: > 0 } n ? n : _account.Name ?? _account.Tag;

    public string Title => _account.Name is { Length: > 0 } real && _account.Nickname is { Length: > 0 } ? $"{Name}  ({real})" : Name;

    public string? Nickname
    {
        get => _account.Nickname;
        set
        {
            _account.Nickname = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            OnPropertyChanged(nameof(Name));
            OnPropertyChanged(nameof(Title));
            _owner.NicknameChanged();
        }
    }

    [ObservableProperty]
    private string _detail = "";

    [ObservableProperty]
    private int _editCount;

    public void Refresh()
    {
        var host = _owner.Host;
        var parts = new List<string> { Tag };
        if (_account.TownHall > 0)
        {
            parts.Add($"Town Hall {_account.TownHall}");
        }

        var export = host.Store.LoadExport(Tag, out var problem);
        parts.Add(export is null
            ? problem is null ? "no export yet" : "export can't be read"
            : export.Timestamp is { } t ? $"export copied {Fmt.Ago(host.Time.GetUtcNow() - t)}" : "export saved");
        EditCount = host.Store.LoadEdits(Tag).Count;
        if (EditCount > 0)
        {
            parts.Add(EditCount == 1 ? "1 hand edit" : $"{EditCount} hand edits");
        }

        Detail = string.Join("  ·  ", parts);
        ClearEditsCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void Import() => _owner.Import(this);

    private bool HasEdits() => EditCount > 0;

    [RelayCommand(CanExecute = nameof(HasEdits))]
    private void ClearEdits() => _owner.ClearEdits(this);

    [RelayCommand]
    private void Remove() => _owner.Remove(this);

    private bool CanMoveUp() => Number > 1;

    [RelayCommand(CanExecute = nameof(CanMoveUp))]
    private void MoveUp() => _owner.Move(this, -1);
}

/// <summary>One game window rule, edited in place.</summary>
internal sealed class RuleRowViewModel(SettingsViewModel owner, GameWindowRule rule) : ObservableObject
{
    public GameWindowRule Rule => rule;

    public bool Enabled
    {
        get => rule.Enabled;
        set
        {
            rule.Enabled = value;
            owner.RulesEdited();
        }
    }

    public string Name
    {
        get => rule.Name;
        set
        {
            rule.Name = value;
            owner.RulesEdited();
        }
    }

    public string? ProcessName
    {
        get => rule.ProcessName;
        set
        {
            rule.ProcessName = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            owner.RulesEdited();
        }
    }

    public string? TitlePattern
    {
        get => rule.TitlePattern;
        set
        {
            rule.TitlePattern = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            owner.RulesEdited();
        }
    }
}
