namespace Np2ptpGui;

using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using Np2ptpGui.Services;
using Application = System.Windows.Application;

public sealed class TrayIconManager : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly Window _mainWindow;
    private readonly TaskManager _taskManager;
    private bool _isExiting;

    public TrayIconManager(Window mainWindow, TaskManager taskManager)
    {
        _mainWindow = mainWindow;
        _taskManager = taskManager;

        var menu = new ContextMenuStrip();
        menu.Items.Add("Open", null, (_, _) => ShowMainWindow());
        menu.Items.Add("Exit", null, async (_, _) => await ExitAsync());

        _notifyIcon = new NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Application,
            Text = "np2ptp",
            Visible = true,
            ContextMenuStrip = menu,
        };
        _notifyIcon.DoubleClick += (_, _) => ShowMainWindow();

        _mainWindow.Closing += OnMainWindowClosing;
    }

    private void OnMainWindowClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_isExiting) return;
        e.Cancel = true;
        _mainWindow.Hide();
    }

    private void ShowMainWindow()
    {
        _mainWindow.Show();
        _mainWindow.WindowState = WindowState.Normal;
        _mainWindow.Activate();
    }

    private async Task ExitAsync()
    {
        _isExiting = true;
        await _taskManager.StopAllServesAsync(TimeSpan.FromSeconds(5));
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        Application.Current.Shutdown();
    }

    public void Dispose() => _notifyIcon.Dispose();
}
