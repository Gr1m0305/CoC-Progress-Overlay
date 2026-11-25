using System.Drawing;
using System.Windows;
using System.Windows.Forms;

namespace ClashOverlay.App.Tray;

/// <summary>What the tray menu can do; the app wires each to its handler.</summary>
internal sealed class TrayActions
{
    public required Action ToggleOverlay { get; init; }

    public required Func<IReadOnlyList<(string Tag, string Label, bool Selected)>> Accounts { get; init; }

    public required Action<string> SelectAccount { get; init; }

    public Action? ImportExport { get; init; }

    public Action? OpenSettings { get; init; }

    public Action? CheckForUpdates { get; init; }

    public required Action Preview { get; init; }

    public required Action OpenLogs { get; init; }

    public Action? About { get; init; }

    public required Action Exit { get; init; }

    public required Func<string> ToggleHint { get; init; }

    /// <summary>The key that jumps to account n (1-9), shown beside it in the menu.</summary>
    public Func<int, string>? AccountHint { get; init; }
}

/// <summary>The notification-area icon and its menu (WinForms, the only WinForms use in the app).</summary>
internal sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly TrayActions _actions;
    private readonly ToolStripMenuItem _toggle;
    private readonly ToolStripMenuItem _accounts;
    private Action? _balloonClick;

    public TrayIcon(TrayActions actions, string tooltip)
    {
        _actions = actions;
        var menu = new ContextMenuStrip { Renderer = new DarkRenderer(), ShowImageMargin = false, Font = new Font("Segoe UI", 9.5f) };
        _toggle = new ToolStripMenuItem("Show overlay", null, (_, _) => actions.ToggleOverlay()) { Font = new Font("Segoe UI", 9.5f, System.Drawing.FontStyle.Bold) };
        _accounts = new ToolStripMenuItem("Account");
        menu.Items.Add(_toggle);
        menu.Items.Add(_accounts);
        menu.Items.Add(new ToolStripSeparator());
        if (actions.ImportExport is { } import)
        {
            menu.Items.Add(new ToolStripMenuItem("Import village export…", null, (_, _) => import()));
        }

        if (actions.OpenSettings is { } settings)
        {
            menu.Items.Add(new ToolStripMenuItem("Settings…", null, (_, _) => settings()));
        }

        if (actions.CheckForUpdates is { } update)
        {
            menu.Items.Add(new ToolStripMenuItem("Update game data and images", null, (_, _) => update()));
        }

        menu.Items.Add(new ToolStripMenuItem("Preview with demo data", null, (_, _) => actions.Preview()));
        menu.Items.Add(new ToolStripMenuItem("Open log folder", null, (_, _) => actions.OpenLogs()));
        if (actions.About is { } about)
        {
            menu.Items.Add(new ToolStripMenuItem("About Clash Tracker", null, (_, _) => about()));
        }

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Exit", null, (_, _) => actions.Exit()));
        menu.Opening += (_, _) => Refresh();

        _icon = new NotifyIcon
        {
            Icon = LoadIcon(),
            Text = tooltip.Length > 63 ? tooltip[..63] : tooltip,
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                actions.ToggleOverlay();
            }
        };
        _icon.BalloonTipClicked += (_, _) =>
        {
            var click = _balloonClick;
            _balloonClick = null;
            click?.Invoke();
        };
        _icon.BalloonTipClosed += (_, _) => _balloonClick = null;
    }

    public ContextMenuStrip Menu => _icon.ContextMenuStrip!;

    public void Refresh()
    {
        _toggle.Text = $"Show / hide overlay\t{_actions.ToggleHint()}";
        _accounts.DropDownItems.Clear();
        var accounts = _actions.Accounts();
        _accounts.Visible = accounts.Count > 0;
        var i = 1;
        foreach (var (tag, label, selected) in accounts)
        {
            var item = new ToolStripMenuItem(label, null, (_, _) => _actions.SelectAccount(tag)) { Checked = selected };
            if (i <= 9)
            {
                item.ShortcutKeyDisplayString = _actions.AccountHint?.Invoke(i) ?? $"Ctrl+{i}";
            }

            _accounts.DropDownItems.Add(item);
            i++;
        }
    }

    /// <summary>A Windows notification; <paramref name="onClick"/> runs if the user clicks it.</summary>
    public void Notify(string title, string text, Action? onClick = null, bool warning = false)
    {
        _balloonClick = onClick;
        _icon.ShowBalloonTip(8000, title, text, warning ? ToolTipIcon.Warning : ToolTipIcon.Info);
    }

    public void SetTooltip(string text) => _icon.Text = text.Length > 63 ? text[..63] : text;

    private static Icon LoadIcon()
    {
        var info = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Assets/app.ico"));
        if (info is null)
        {
            return SystemIcons.Application;
        }

        using var stream = info.Stream;
        return new Icon(stream, SystemInformation.SmallIconSize);
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }

    /// <summary>Dark menu in the overlay's colours.</summary>
    private sealed class DarkRenderer : ToolStripProfessionalRenderer
    {
        private static readonly Color Back = Color.FromArgb(24, 29, 37);
        private static readonly Color Hover = Color.FromArgb(44, 51, 63);
        private static readonly Color Text = Color.FromArgb(242, 244, 248);
        private static readonly Color Muted = Color.FromArgb(150, 158, 172);

        public DarkRenderer()
            : base(new Colors())
        {
            RoundedEdges = false;
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? Text : Muted;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = Muted;
            base.OnRenderArrow(e);
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            var r = new Rectangle(e.ImageRectangle.Left + 4, e.ImageRectangle.Top + e.ImageRectangle.Height / 2 - 3, 6, 6);
            using var b = new SolidBrush(Color.FromArgb(245, 197, 66));
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            e.Graphics.FillEllipse(b, r);
        }

        private sealed class Colors : ProfessionalColorTable
        {
            public override Color ToolStripDropDownBackground => Back;

            public override Color MenuBorder => Color.FromArgb(58, 66, 80);

            public override Color MenuItemBorder => Hover;

            public override Color MenuItemSelected => Hover;

            public override Color MenuItemSelectedGradientBegin => Hover;

            public override Color MenuItemSelectedGradientEnd => Hover;

            public override Color MenuItemPressedGradientBegin => Hover;

            public override Color MenuItemPressedGradientEnd => Hover;

            public override Color ImageMarginGradientBegin => Back;

            public override Color ImageMarginGradientMiddle => Back;

            public override Color ImageMarginGradientEnd => Back;

            public override Color SeparatorDark => Color.FromArgb(52, 59, 72);

            public override Color SeparatorLight => Back;

            public override Color CheckBackground => Back;

            public override Color CheckSelectedBackground => Hover;
        }
    }
}
