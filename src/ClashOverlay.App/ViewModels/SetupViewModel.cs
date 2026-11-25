using System.Collections.ObjectModel;
using ClashOverlay.Api;
using ClashOverlay.App.Services;
using ClashOverlay.Core.Settings;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ClashOverlay.App.ViewModels;

internal sealed record SetupStep(int Number, string Title, string Subtitle);

/// <summary>
/// First-run setup: (1) create a key for this PC's public IP, (2) paste and test it, (3) add accounts, (4) import the
/// village export. Also opened at step 1 when the IP changed or the key was refused.
/// </summary>
internal sealed partial class SetupViewModel : ObservableObject
{
    private readonly AppHost _host;
    private readonly KeyWatcher? _keys;

    public SetupViewModel(AppHost host, KeyWatcher? keys, int startStep = 1)
    {
        _host = host;
        _keys = keys;
        Import = new ImportViewModel(host);
        Lookup = new AccountLookupViewModel(host);
        Lookup.Added += (_, a) =>
        {
            Accounts.Add(a);
            Import.Refresh();
            NextCommand.NotifyCanExecuteChanged();
        };
        foreach (var a in host.Settings.Accounts)
        {
            Accounts.Add(a);
        }

        _hasSavedKey = host.Keys.HasKey;
        _useDeveloperLogin = host.Settings.DeveloperSiteAutoKey;
        _developerEmail = host.Logins.Load()?.Email ?? "";
        _step = Math.Clamp(startStep, 1, 4);
        _ = FindIpAsync();
    }

    public event EventHandler? Finished;

    public IReadOnlyList<SetupStep> Steps { get; } =
    [
        new(1, "Create your API key", "On the developer site"),
        new(2, "Paste and test it", "Kept encrypted on this PC"),
        new(3, "Add your accounts", "By player tag"),
        new(4, "Import your village", "The in-game data export"),
    ];

    public AccountLookupViewModel Lookup { get; }

    public ImportViewModel Import { get; }

    public ObservableCollection<AccountSettings> Accounts { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsStep1), nameof(IsStep2), nameof(IsStep3), nameof(IsStep4), nameof(NextText), nameof(SelectedStep))]
    [NotifyCanExecuteChangedFor(nameof(BackCommand), nameof(NextCommand))]
    private int _step;

    public bool IsStep1 => Step == 1;

    public bool IsStep2 => Step == 2;

    public bool IsStep3 => Step == 3;

    public bool IsStep4 => Step == 4;

    public SetupStep SelectedStep
    {
        get => Steps[Step - 1];
        set
        {
            if (value is not null)
            {
                Step = value.Number;
            }
        }
    }

    public string NextText => Step == 4 ? "Finish" : "Next";

    // ----- step 1: the developer site and this PC's IP -----

    [ObservableProperty]
    private string? _publicIp;

    [ObservableProperty]
    private string _ipText = "Finding this PC's public IP address…";

    [ObservableProperty]
    private string? _copyText;

    private async Task FindIpAsync()
    {
        if (_host.IsDemo)
        {
            PublicIp = "203.0.113.24";
            IpText = PublicIp;
            return;
        }

        PublicIp = await _host.PublicIp.GetAsync();
        IpText = PublicIp ?? "Could not find it. Search the web for \"what is my IP\" and use that address.";
        if (PublicIp is not null && _host.Settings.LastKnownIp != PublicIp)
        {
            _host.Settings.LastKnownIp = PublicIp;
            _host.SaveSettings();
        }
    }

    [RelayCommand]
    private void OpenSite() => Shell.Open(Shell.DeveloperSite);

    [RelayCommand]
    private void CopyIp()
    {
        if (PublicIp is not null)
        {
            CopyText = Shell.TryCopy(PublicIp) ? "Copied" : "Copy failed";
        }
    }

    // ----- step 2: paste and test -----

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(TestKeyCommand))]
    private string _keyText = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(TestKeyCommand), nameof(MakeKeyCommand))]
    private bool _isTesting;

    [ObservableProperty]
    private string? _keyResult;

    [ObservableProperty]
    private bool _keyOk;

    [ObservableProperty]
    private bool _hasSavedKey;

    partial void OnKeyTextChanged(string value) => KeyResult = null;

    private bool CanTest() => !IsTesting && KeyText.Trim().Length > 0;

    [RelayCommand(CanExecute = nameof(CanTest))]
    private async Task TestKey()
    {
        var key = ApiKeyStore.Normalize(KeyText);
        if (!ApiKeyInfo.LooksLikeKey(key))
        {
            Show(false, "That doesn't look like an API key. On the developer site open your key and copy the long token under \"Key\".");
            return;
        }

        var info = ApiKeyInfo.TryParse(key);
        var ipNote = info is not null && PublicIp is not null && !info.Allows(PublicIp)
            ? $" This key is for {string.Join(", ", info.AllowedIps)}, but this PC's address is {PublicIp}: make a new key with {PublicIp}."
            : "";

        IsTesting = true;
        try
        {
            var problem = await _host.Api.CheckKeyAsync(key);
            if (problem is null)
            {
                _host.Keys.Save(key);
                _host.Api.ResetBackoff();
                HasSavedKey = true;
                KeyText = "";
                Show(true, "The key works. It is saved, encrypted for your Windows account.");
            }
            else
            {
                Show(false, problem.Message + (problem.Kind == ApiErrorKind.InvalidIp ? ipNote : ""));
            }
        }
        finally
        {
            IsTesting = false;
        }
    }

    // Optional: let the app make keys with the user's developer-site login.

    [ObservableProperty]
    private bool _useDeveloperLogin;

    [ObservableProperty]
    private string _developerEmail;

    partial void OnUseDeveloperLoginChanged(bool value)
    {
        _host.Settings.DeveloperSiteAutoKey = value;
        if (!value)
        {
            _host.Logins.Delete();
        }

        _host.SaveSettings();
    }

    private bool CanMakeKey() => !IsTesting && _keys is not null;

    /// <summary>The password comes from the PasswordBox (not bound, so it never sits in a view-model property).</summary>
    [RelayCommand(CanExecute = nameof(CanMakeKey))]
    private async Task MakeKey(System.Windows.Controls.PasswordBox? box)
    {
        var password = box?.Password ?? "";
        if (string.IsNullOrWhiteSpace(DeveloperEmail) || password.Length == 0)
        {
            Show(false, "Enter the email and password you use on developer.clashofclans.com.");
            return;
        }

        IsTesting = true;
        try
        {
            var login = new DeveloperLogin(DeveloperEmail.Trim(), password);
            var message = await _keys!.MakeKeyAsync(login, PublicIp, CancellationToken.None);
            _host.Logins.Save(login);
            box?.Clear();
            HasSavedKey = true;
            Show(true, message + " Your login is saved, encrypted for your Windows account, for the next IP change.");
        }
        catch (Exception e) when (e is DeveloperSiteException or HttpRequestException or TaskCanceledException)
        {
            Show(false, e.Message);
        }
        finally
        {
            IsTesting = false;
        }
    }

    private void Show(bool ok, string text)
    {
        KeyOk = ok;
        KeyResult = text;
    }

    // ----- step 3: accounts -----

    [RelayCommand]
    private void RemoveAccount(AccountSettings? account)
    {
        if (account is null)
        {
            return;
        }

        _host.Settings.Accounts.RemoveAll(a => a.Tag == account.Tag);
        _host.Store.Delete(account.Tag);
        _host.SaveSettings();
        Accounts.Remove(account);
        Import.Refresh();
        NextCommand.NotifyCanExecuteChanged();
    }

    // ----- navigation -----

    private bool CanBack() => Step > 1;

    [RelayCommand(CanExecute = nameof(CanBack))]
    private void Back() => Step--;

    private bool CanNext() => Step != 3 || Accounts.Count > 0;

    [RelayCommand(CanExecute = nameof(CanNext))]
    private void Next()
    {
        if (Step < 4)
        {
            Step++;
            if (Step == 4)
            {
                Import.Refresh();
            }

            return;
        }

        _host.Settings.SetupCompleted = true;
        _host.SaveSettings();
        Finished?.Invoke(this, EventArgs.Empty);
    }
}
