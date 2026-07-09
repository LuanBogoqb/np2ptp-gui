namespace Np2ptpGui;

using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Windows;
using Np2ptpGui.Services;
using Np2ptpGui.ViewModels;

public partial class App : System.Windows.Application
{
    private TrayIconManager? _trayIconManager;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var appDataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "np2ptp-gui");
        var binsDir = Path.Combine(AppContext.BaseDirectory, "bins");

        var configStore = new ConfigStore(appDataDir);
        var historyStore = new HistoryStore(appDataDir);
        var config = configStore.Load();

        var httpClient = new HttpClient();
        var releaseClient = new GitHubReleaseClient(httpClient);
        var binaryManager = new BinaryManager(releaseClient, binsDir);

        try
        {
            await binaryManager.EnsureBinaryAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                $"Could not download np2ptp.exe and no local copy exists:\n{ex.Message}",
                "np2ptp-gui", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
            return;
        }

        await binaryManager.TryCheckForUpdateSilentlyAsync(TimeSpan.FromSeconds(3));

        try
        {
            if (string.IsNullOrEmpty(config.DefaultDownloadFolder))
            {
                config.DefaultDownloadFolder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            }
            if (string.IsNullOrEmpty(config.StoreFolder))
            {
                config.StoreFolder = Path.Combine(appDataDir, "store");
            }
            config.BinaryPath = binaryManager.ExePath;
            configStore.Save(config);

            historyStore.MarkRunningAsInterrupted();

            var taskManager = new TaskManager(
                binaryManager.ExePath,
                historyStore,
                uiDispatch: action => Dispatcher.Invoke(action));
            var mainViewModel = new MainViewModel(taskManager, config);
            var settingsViewModel = new SettingsViewModel(configStore, binaryManager, config);

            var themeService = new WindowsThemeService();
            Np2ptpGui.Themes.ThemeManager.Initialize(themeService.IsLightTheme());
            themeService.ThemeChanged += isLight => Np2ptpGui.Themes.ThemeManager.ApplyTheme(isLight);

            var mainWindow = new MainWindow { DataContext = mainViewModel };
            mainWindow.SettingsTab.DataContext = settingsViewModel;

            _trayIconManager = new TrayIconManager(mainWindow, taskManager);

            MainWindow = mainWindow;
            mainWindow.Show();
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                $"np2ptp-gui failed to start:\n{ex.Message}",
                "np2ptp-gui", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
        }
    }
}
