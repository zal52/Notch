using System;
using System.Drawing;
using System.Windows;
using Forms = System.Windows.Forms;

namespace Notch.Infrastructure.Windows;

public sealed class TrayIcon : IDisposable
{
    private readonly Forms.NotifyIcon _icon;
    private readonly Forms.ContextMenuStrip _menu;
    private bool _disposed;

    public TrayIcon(Action show, Action toggle, Action exit)
    {
        void Dispatch(Action action) => Application.Current.Dispatcher.BeginInvoke(action);
        _menu = new Forms.ContextMenuStrip();
        _menu.Items.Add("Открыть Notch", null, (_, _) => Dispatch(show));
        _menu.Items.Add("Показать / скрыть", null, (_, _) => Dispatch(toggle));
        _menu.Items.Add(new Forms.ToolStripSeparator());
        _menu.Items.Add("Выйти", null, (_, _) => Dispatch(exit));
        _icon = new Forms.NotifyIcon
        {
            Text = "Notch", Icon = SystemIcons.Application, ContextMenuStrip = _menu, Visible = true
        };
        _icon.DoubleClick += (_, _) => Dispatch(show);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _icon.Visible = false;
        _icon.Dispose();
        _menu.Dispose();
    }

    public void ShowNotice(string message) =>
        _icon.ShowBalloonTip(5000, "Notch", message, Forms.ToolTipIcon.Warning);
}
