using ClashOverlay.App.Services;
using ClashOverlay.Core.Data;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ClashOverlay.App.ViewModels;

/// <summary>One tab of the overlay. Each type has a DataTemplate in Views/Tabs.xaml.</summary>
internal abstract class TabViewModel : ObservableObject
{
    /// <summary>Stable id, so the same tab stays selected across refreshes.</summary>
    public abstract string Key { get; }

    public abstract string Title { get; }

    /// <summary>Once a second while the overlay is open, for countdowns.</summary>
    public virtual void Tick(DateTimeOffset now)
    {
    }

    /// <summary>Clan, war, raid and Gold Pass data arrived.</summary>
    public virtual void OnProfile(ProfileData profile)
    {
    }
}

/// <summary>Shown when there is nothing to compute yet (no account, no data).</summary>
internal sealed class MessageTab(string key, string title, string message, string? detail = null) : TabViewModel
{
    public override string Key => key;

    public override string Title => title;

    public string Message => message;

    public string? Detail => detail;
}

internal static class TabFactory
{
    public static IEnumerable<TabViewModel> Create(OverlayViewModel owner, AccountData? data, Village village)
    {
        if (owner.SelectedAccount is null)
        {
            return [new MessageTab("overview", "Overview", "No account yet", "Open Settings from the tray icon and add your player tag.")];
        }

        if (data is null)
        {
            return [new MessageTab("overview", "Overview", "Loading…")];
        }

        if (owner.Host.Data.Data is not { } game)
        {
            return [new MessageTab("overview", "Overview", "Game data is not downloaded yet", owner.Host.Data.Problem)];
        }

        if (data.Report(village) is not { } report)
        {
            return village == Village.BuilderBase
                ? [new MessageTab("overview", "Overview", "No Builder Base yet", "The Builder Base appears once the boat is repaired.")]
                : [new MessageTab("overview", "Overview", "No village data", "The API has not answered yet and there is no export. Import your village export from the tray menu.")];
        }

        var ctx = new TabContext(owner, data, report, game);
        return village == Village.Home
            ?
            [
                new OverviewTab(ctx),
                new RushedTab(ctx),
                new UpgradesTab(ctx, UpgradeList.Builder),
                new UpgradesTab(ctx, UpgradeList.Laboratory),
                new HeroesTab(ctx),
                new EquipmentTab(ctx),
                new ProfileTab(ctx),
            ]
            :
            [
                new OverviewTab(ctx),
                new UpgradesTab(ctx, UpgradeList.Builder),
                new UpgradesTab(ctx, UpgradeList.Laboratory),
                new ProfileTab(ctx),
            ];
    }
}
