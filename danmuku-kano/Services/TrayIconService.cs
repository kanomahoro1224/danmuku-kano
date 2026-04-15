using System;
using System.Windows.Forms;

namespace damuku_kano.Services;

public sealed class TrayIconService : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly ToolStripMenuItem _showItem;
    private readonly ToolStripMenuItem _exitItem;
    private bool _isDisposed;

    public event EventHandler? ShowRequested;
    public event EventHandler? ExitRequested;

    public TrayIconService()
    {
        _showItem = new ToolStripMenuItem();
        _exitItem = new ToolStripMenuItem();

        var contextMenu = new ContextMenuStrip();
        contextMenu.Items.Add(_showItem);
        contextMenu.Items.Add(_exitItem);

        _notifyIcon = new NotifyIcon
        {
            Icon = AppIconHelper.GetApplicationIcon(),
            Visible = true,
            ContextMenuStrip = contextMenu
        };

        _notifyIcon.DoubleClick += (_, _) => ShowRequested?.Invoke(this, EventArgs.Empty);
        _showItem.Click += (_, _) => ShowRequested?.Invoke(this, EventArgs.Empty);
        _exitItem.Click += (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty);

        UpdateStrings();
    }

    public void UpdateStrings()
    {
        if (_isDisposed)
        {
            return;
        }

        _notifyIcon.Text = LocalizationService.Instance.AppTitle;
        _showItem.Text = LocalizationService.Instance.TrayAppShow;
        _exitItem.Text = LocalizationService.Instance.TrayAppExit;
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _isDisposed = true;
    }
}
