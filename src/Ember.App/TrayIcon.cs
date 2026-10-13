using System.Windows.Forms;
using Ember.Core;

namespace Ember.App;

/// <summary>
/// The icon in the notification area: double-click or "Open Ember" brings the window back, the fan mode items change the
/// mode, and "Quit Ember" really closes the app. Fan control itself runs in the service and is not affected by any of this.
/// </summary>
public sealed class TrayIcon : IDisposable
{
    readonly NotifyIcon _icon;
    readonly ToolStripMenuItem[] _modes;
    readonly Action _open, _quit;

    public TrayIcon(Action open, Action quit, Action<FanMode> setMode)
    {
        _open = open; _quit = quit;
        var menu = new ContextMenuStrip();
        menu.Items.Add("Open Ember", null, (_, _) => _open());
        menu.Items.Add(new ToolStripSeparator());
        var order = new[] { FanMode.SuperQuiet, FanMode.Quiet, FanMode.Normal, FanMode.Performance, FanMode.Extreme };
        _modes = order.Select(m =>
        {
            var item = new ToolStripMenuItem(FansViewState.Label(m)) { Tag = m };
            item.Click += (_, _) => setMode(m);
            return item;
        }).ToArray();
        foreach (var item in _modes) menu.Items.Add(item);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Quit Ember", null, (_, _) => _quit());

        _icon = new NotifyIcon
        {
            Icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? System.Drawing.SystemIcons.Application,
            Text = "Ember",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.DoubleClick += (_, _) => _open();
    }

    /// <summary>Ticks the current mode in the menu and shows it in the tooltip (a tooltip holds at most 63 characters).</summary>
    public void SetMode(FanMode mode, string? summary)
    {
        foreach (var item in _modes) item.Checked = item.Tag is FanMode m && m == mode;
        var text = "Ember: " + FansViewState.Label(mode) + (string.IsNullOrEmpty(summary) ? "" : " - " + summary);
        _icon.Text = text.Length <= 63 ? text : text[..63];
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
