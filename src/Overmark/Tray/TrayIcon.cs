using System.Drawing;
using System.Windows.Forms;
using Overmark.Hotkeys;

namespace Overmark.Tray;

/// <summary>The notification-area icon, its right-click menu and its pop-up notifications.</summary>
internal sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly Icon _icon;
    private readonly Dictionary<HotkeyAction, ToolStripMenuItem> _actionItems = [];
    private Action? _notificationClicked;

    public TrayIcon(Action<HotkeyAction> runAction, Action editLastCapture, Action showDisplayInfo, Action openSettings, Action quit)
    {
        var menu = new ContextMenuStrip();
        foreach (var action in Enum.GetValues<HotkeyAction>())
        {
            var item = new ToolStripMenuItem(action.DisplayName(), null, (_, _) => runAction(action));
            _actionItems[action] = item;
            menu.Items.Add(item);
        }

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Edit last capture", null, (_, _) => editLastCapture());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Display info…", null, (_, _) => showDisplayInfo());
        menu.Items.Add("Settings…", null, (_, _) => openSettings());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Quit", null, (_, _) => quit());

        _icon = LoadIcon();
        _notifyIcon = new NotifyIcon
        {
            Icon = _icon,
            Text = "Overmark",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _notifyIcon.MouseDoubleClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                runAction(HotkeyAction.RegionCapture);
            }
        };
        _notifyIcon.BalloonTipClicked += (_, _) => _notificationClicked?.Invoke();
    }

    /// <summary>Shows each action's hotkey beside it in the menu.</summary>
    public void ShowHotkeys(IReadOnlyDictionary<HotkeyAction, Hotkey> bindings)
    {
        foreach (var (action, item) in _actionItems)
        {
            item.ShortcutKeyDisplayString = bindings.TryGetValue(action, out var hotkey) ? hotkey.DisplayText : "";
        }
    }

    /// <summary>Shows a notification. <paramref name="onClick"/> runs if the user clicks it.</summary>
    public void Notify(string title, string text, Action? onClick = null)
    {
        _notificationClicked = onClick;
        _notifyIcon.ShowBalloonTip(5000, title, text, ToolTipIcon.None);
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.ContextMenuStrip?.Dispose();
        _notifyIcon.Dispose();
        _icon.Dispose();
    }

    private static Icon LoadIcon()
    {
        var resource = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Assets/Overmark.ico"));
        using var stream = resource.Stream;
        return new Icon(stream, SystemInformation.SmallIconSize);
    }
}
