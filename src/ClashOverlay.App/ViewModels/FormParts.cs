using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using ClashOverlay.Api;
using ClashOverlay.Api.Models;
using ClashOverlay.App.Services;
using ClashOverlay.Core;
using ClashOverlay.Core.Assets;
using ClashOverlay.Core.Data;
using ClashOverlay.Core.Display;
using ClashOverlay.Core.Export;
using ClashOverlay.Core.Settings;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;

namespace ClashOverlay.App.ViewModels;

internal static class Shell
{
    public const string DeveloperSite = "https://developer.clashofclans.com";

    public static void Open(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
        }
    }

    public static bool TryCopy(string text)
    {
        try
        {
            Clipboard.SetText(text);
            return true;
        }
        catch (COMException)
        {
            return false;
        }
    }

    public static string? TryPaste()
    {
        try
        {
            return Clipboard.ContainsText() ? Clipboard.GetText() : null;
        }
        catch (COMException)
        {
            return null;
        }
    }
}

/// <summary>"Add an account": a player tag, looked up in the API to preview name, Town Hall, league and clan.</summary>
internal sealed partial class AccountLookupViewModel(AppHost host) : ObservableObject
{
    public event EventHandler<AccountSettings>? Added;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LookUpCommand))]
    private string _tagText = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LookUpCommand), nameof(AddCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private string? _problem;

    /// <summary>The lookup failed for a reason other than a wrong tag, so the account can be added without a preview.</summary>
    [ObservableProperty]
    private bool _canAddAnyway;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPreview))]
    [NotifyCanExecuteChangedFor(nameof(AddCommand))]
    private PlayerDto? _player;

    [ObservableProperty]
    private ImageSource? _hallImage;

    [ObservableProperty]
    private ImageSource? _leagueImage;

    [ObservableProperty]
    private string? _previewTitle;

    [ObservableProperty]
    private string? _previewDetail;

    [ObservableProperty]
    private string? _previewClan;

    public bool HasPreview => Player is not null;

    partial void OnTagTextChanged(string value)
    {
        Player = null;
        Problem = null;
        CanAddAnyway = false;
    }

    private bool CanLookUp() => !IsBusy && TagText.Trim().Length > 0;

    [RelayCommand(CanExecute = nameof(CanLookUp))]
    private async Task LookUp()
    {
        Problem = null;
        Player = null;
        CanAddAnyway = false;
        var tag = PlayerTag.Normalize(TagText);
        if (!PlayerTag.IsValid(tag))
        {
            Problem = "That isn't a player tag. Tags start with # and use only 0 2 8 9 P Y L Q G R J C U V. Find yours under your name on your profile in the game.";
            return;
        }

        if (host.Settings.Accounts.Any(a => a.Tag == tag))
        {
            Problem = "That account is already in the list.";
            return;
        }

        IsBusy = true;
        try
        {
            var result = await host.Api.GetPlayerAsync(tag, force: true);
            if (result.Value is { } p)
            {
                Player = p;
                var game = host.Data.Data;
                HallImage = game is null ? null : host.Images.Item(game.Hall(Village.Home), p.TownHallLevel, 96);
                LeagueImage = p.LeagueTier is { } l ? host.Images.Remote(AssetPaths.LeagueTier(l.Name), 96) : null;
                PreviewTitle = $"{p.Name}  ·  Town Hall {p.TownHallLevel}";
                PreviewDetail = $"{p.LeagueTier?.Name ?? "Unranked"} · {p.Trophies.ToString("N0", CultureInfo.InvariantCulture)} trophies · XP {p.ExpLevel}"
                    + (p.BuilderHallLevel > 0 ? $" · Builder Hall {p.BuilderHallLevel}" : "");
                PreviewClan = p.Clan is { } c ? $"Clan: {c.Name} ({c.Tag})" : "Not in a clan";
            }
            else
            {
                var e = result.Error;
                Problem = e?.Kind == ApiErrorKind.NotFound
                    ? "No player has that tag. Check it on your profile in the game."
                    : $"Could not check the tag: {e?.Message ?? "no answer from the API."}";
                CanAddAnyway = e?.Kind != ApiErrorKind.NotFound;
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanAdd() => !IsBusy && Player is not null;

    [RelayCommand(CanExecute = nameof(CanAdd))]
    private void Add() => AddAccount(PlayerTag.Normalize(Player!.Tag), Player.Name, Player.TownHallLevel);

    [RelayCommand]
    private void AddAnyway() => AddAccount(PlayerTag.Normalize(TagText), null, 0);

    private void AddAccount(string tag, string? name, int townHall)
    {
        if (host.Settings.Accounts.Any(a => a.Tag == tag))
        {
            return;
        }

        var account = new AccountSettings { Tag = tag, Name = name, TownHall = townHall };
        host.Settings.Accounts.Add(account);
        host.Settings.SelectedAccount ??= tag;
        host.SaveSettings();
        TagText = "";
        Added?.Invoke(this, account);
    }
}

/// <summary>Importing the village export for one account: paste, drop a file, or choose one.</summary>
internal sealed partial class ImportViewModel : ObservableObject
{
    public const string ExportSteps = "In Clash of Clans open Settings → More Settings → Data Export and tap Copy. Then paste it here, drop the saved file on this box, or choose the file.";

    private readonly AppHost _host;

    public ImportViewModel(AppHost host, string? tag = null)
    {
        _host = host;
        foreach (var a in host.Settings.Accounts)
        {
            Accounts.Add(a);
        }

        Account = Accounts.FirstOrDefault(a => a.Tag == (tag ?? host.Settings.SelectedAccount)) ?? Accounts.FirstOrDefault();
    }

    public ObservableCollection<AccountSettings> Accounts { get; } = [];

    public bool ManyAccounts => Accounts.Count > 1;

    public string Steps => ExportSteps;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentExport))]
    private AccountSettings? _account;

    [ObservableProperty]
    private string? _result;

    [ObservableProperty]
    private bool _ok;

    /// <summary>What is stored for the chosen account now.</summary>
    public string CurrentExport
    {
        get
        {
            if (Account is null)
            {
                return "Add an account first.";
            }

            var export = _host.Store.LoadExport(Account.Tag, out var problem);
            if (export is null)
            {
                return problem is null ? "No export saved for this account yet." : $"The saved export can't be read: {problem}";
            }

            return export.Timestamp is { } t
                ? $"Saved export: copied {Fmt.Ago(_host.Time.GetUtcNow() - t)} ({t.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)})."
                : "Saved export: no date in it.";
        }
    }

    public void Refresh()
    {
        Accounts.Clear();
        foreach (var a in _host.Settings.Accounts)
        {
            Accounts.Add(a);
        }

        Account = Accounts.FirstOrDefault(a => a.Tag == Account?.Tag) ?? Accounts.FirstOrDefault();
        OnPropertyChanged(nameof(ManyAccounts));
        OnPropertyChanged(nameof(CurrentExport));
    }

    /// <summary>Lists the accounts again and picks <paramref name="tag"/> when given.</summary>
    public void Select(string? tag)
    {
        Refresh();
        if (tag is not null && Accounts.FirstOrDefault(a => a.Tag == tag) is { } a)
        {
            Account = a;
        }

        Result = null;
    }

    [RelayCommand]
    private void Paste()
    {
        var text = Shell.TryPaste();
        if (string.IsNullOrWhiteSpace(text))
        {
            Show(false, "The clipboard has no text. Tap Copy under Data Export in the game first.");
            return;
        }

        Import(text);
    }

    [RelayCommand]
    private void ChooseFile()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Choose the village export",
            Filter = "Village export (*.json;*.txt)|*.json;*.txt|All files (*.*)|*.*",
        };
        if (dialog.ShowDialog() == true)
        {
            ImportFile(dialog.FileName);
        }
    }

    public void ImportFile(string path)
    {
        try
        {
            if (new FileInfo(path).Length > 8 * 1024 * 1024)
            {
                Show(false, "That file is too big to be a village export.");
                return;
            }

            Import(File.ReadAllText(path));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Show(false, $"The file could not be read: {e.Message}");
        }
    }

    /// <summary>Text dropped on the box.</summary>
    public void ImportText(string json) => Import(json);

    private void Import(string json)
    {
        if (Account is null)
        {
            Show(false, "Add an account first.");
            return;
        }

        try
        {
            // An export of another added account goes to that account.
            var parsed = ExportParser.Parse(json);
            var tag = PlayerTag.Normalize(parsed.Tag);
            var target = _host.Settings.Accounts.FirstOrDefault(a => a.Tag == tag);
            if (target is null)
            {
                Show(false, $"This export is for {tag}, which isn't one of your accounts. Add it first, then import again.");
                return;
            }

            var export = _host.Store.Import(json, target.Tag);
            Account = Accounts.FirstOrDefault(a => a.Tag == target.Tag) ?? Account;
            var when = export.Timestamp is { } t ? $"copied {Fmt.Ago(_host.Time.GetUtcNow() - t)}" : "with no date";
            var name = target.Nickname is { Length: > 0 } n ? n : target.Name ?? target.Tag;
            Show(true, $"Imported {name}'s village ({export.Entries.Count} entries, {when}).");
            OnPropertyChanged(nameof(CurrentExport));
        }
        catch (ExportFormatException e)
        {
            Show(false, e.Message);
        }
    }

    private void Show(bool ok, string text)
    {
        Ok = ok;
        Result = text;
    }
}
