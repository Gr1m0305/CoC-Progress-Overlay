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
internal sealed class MessageTab(string key, string title, string message) : TabViewModel
{
    public override string Key => key;

    public override string Title => title;

    public string Message => message;
}

internal static class TabFactory
{
    public static IEnumerable<TabViewModel> Create(OverlayViewModel owner, AccountData? data, Village village)
    {
        if (data is null)
        {
            yield return new MessageTab("overview", "Overview", owner.SelectedAccount is null ? "No account yet." : "Loading…");
            yield break;
        }

        yield return new MessageTab("overview", "Overview", $"{data.Name}: {village}");
    }
}
