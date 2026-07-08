namespace Np2ptpGui.ViewModels;

using System;
using System.IO;
using System.Threading;
using Np2ptpGui.Models;
using Np2ptpGui.Services;

public sealed class SettingsViewModel : ViewModelBase
{
    private readonly ConfigStore _configStore;
    private readonly BinaryManager _binaryManager;
    private readonly AppConfig _config;

    private string _binaryPath;
    public string BinaryPath { get => _binaryPath; set => SetField(ref _binaryPath, value); }

    private string _defaultDownloadFolder;
    public string DefaultDownloadFolder { get => _defaultDownloadFolder; set => SetField(ref _defaultDownloadFolder, value); }

    private string _storeFolder;
    public string StoreFolder { get => _storeFolder; set => SetField(ref _storeFolder, value); }

    private string _defaultListenAddress;
    public string DefaultListenAddress { get => _defaultListenAddress; set => SetField(ref _defaultListenAddress, value); }

    private string _trackerUrl;
    public string TrackerUrl { get => _trackerUrl; set => SetField(ref _trackerUrl, value); }

    private bool _alwaysUseDownloadDefaults;
    public bool AlwaysUseDownloadDefaults { get => _alwaysUseDownloadDefaults; set => SetField(ref _alwaysUseDownloadDefaults, value); }

    private bool _keepStoreByDefault;
    public bool KeepStoreByDefault { get => _keepStoreByDefault; set => SetField(ref _keepStoreByDefault, value); }

    private string _updateStatus = "";
    public string UpdateStatus { get => _updateStatus; set => SetField(ref _updateStatus, value); }

    public RelayCommand SaveCommand { get; }
    public RelayCommand CheckForUpdateCommand { get; }
    public RelayCommand BrowseDownloadFolderCommand { get; }
    public RelayCommand BrowseStoreFolderCommand { get; }

    public SettingsViewModel(ConfigStore configStore, BinaryManager binaryManager, AppConfig config)
    {
        _configStore = configStore;
        _binaryManager = binaryManager;
        _config = config;

        _binaryPath = binaryManager.ExePath;
        _defaultDownloadFolder = config.DefaultDownloadFolder;
        _storeFolder = config.StoreFolder;
        _defaultListenAddress = config.DefaultListenAddress;
        _trackerUrl = config.TrackerUrl;
        _alwaysUseDownloadDefaults = config.AlwaysUseDownloadDefaults;
        _keepStoreByDefault = config.KeepStoreByDefault;

        SaveCommand = new RelayCommand(_ =>
        {
            _config.DefaultDownloadFolder = DefaultDownloadFolder;
            _config.StoreFolder = StoreFolder;
            _config.DefaultListenAddress = DefaultListenAddress;
            _config.TrackerUrl = TrackerUrl;
            _config.AlwaysUseDownloadDefaults = AlwaysUseDownloadDefaults;
            _config.KeepStoreByDefault = KeepStoreByDefault;
            _configStore.Save(_config);
        });

        CheckForUpdateCommand = new RelayCommand(async _ =>
        {
            UpdateStatus = "checking...";
            try
            {
                var updated = await _binaryManager.CheckForUpdateAsync(CancellationToken.None);
                UpdateStatus = updated ? "updated to the latest release" : "already up to date";
            }
            catch (Exception ex)
            {
                UpdateStatus = $"check failed: {ex.Message}";
            }
        });

        BrowseDownloadFolderCommand = new RelayCommand(_ =>
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog
            {
                InitialDirectory = Directory.Exists(DefaultDownloadFolder) ? DefaultDownloadFolder : "",
            };
            if (dialog.ShowDialog() == true) DefaultDownloadFolder = dialog.FolderName;
        });

        BrowseStoreFolderCommand = new RelayCommand(_ =>
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog
            {
                InitialDirectory = Directory.Exists(StoreFolder) ? StoreFolder : "",
            };
            if (dialog.ShowDialog() == true) StoreFolder = dialog.FolderName;
        });
    }
}
