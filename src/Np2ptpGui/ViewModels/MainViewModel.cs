namespace Np2ptpGui.ViewModels;

using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using Np2ptpGui.Models;
using Np2ptpGui.Services;
using Np2ptpGui.Views;

public sealed class MainViewModel : ViewModelBase
{
    private readonly TaskManager _taskManager;
    private readonly AppConfig _config;
    private readonly CollectionViewSource _fetchViewSource = new();
    private readonly CollectionViewSource _serveViewSource = new();
    private readonly CollectionViewSource _packViewSource = new();

    public ICollectionView FetchOperations => _fetchViewSource.View;
    public ICollectionView ServeOperations => _serveViewSource.View;
    public ICollectionView PackOperations => _packViewSource.View;

    private string _downloadLinkInput = "";
    public string DownloadLinkInput { get => _downloadLinkInput; set => SetField(ref _downloadLinkInput, value); }

    private string _packInputPath = "";
    public string PackInputPath { get => _packInputPath; set => SetField(ref _packInputPath, value); }

    private string _serveFilePath = "";
    public string ServeFilePath { get => _serveFilePath; set => SetField(ref _serveFilePath, value); }

    public RelayCommand StartDownloadCommand { get; }
    public RelayCommand StartPackCommand { get; }
    public RelayCommand StartServeCommand { get; }
    public RelayCommand StopOperationCommand { get; }
    public RelayCommand RetryOperationCommand { get; }
    public RelayCommand BrowsePackFileCommand { get; }
    public RelayCommand BrowsePackFolderCommand { get; }

    public MainViewModel(TaskManager taskManager, AppConfig config)
    {
        _taskManager = taskManager;
        _config = config;

        _fetchViewSource.Source = _taskManager.Operations;
        _fetchViewSource.Filter += (_, e) => e.Accepted = e.Item is OperationViewModel vm && vm.Type == OperationType.Fetch;
        _serveViewSource.Source = _taskManager.Operations;
        _serveViewSource.Filter += (_, e) => e.Accepted = e.Item is OperationViewModel vm && vm.Type == OperationType.Serve;
        _packViewSource.Source = _taskManager.Operations;
        _packViewSource.Filter += (_, e) => e.Accepted = e.Item is OperationViewModel vm && vm.Type == OperationType.Pack;

        StartDownloadCommand = new RelayCommand(_ =>
        {
            if (string.IsNullOrWhiteSpace(DownloadLinkInput)) return;

            string reconstructFolder;
            string storeFolder;
            bool keepStore;
            if (_config.AlwaysUseDownloadDefaults)
            {
                reconstructFolder = _config.DefaultDownloadFolder;
                storeFolder = _config.StoreFolder;
                keepStore = _config.KeepStoreByDefault;
            }
            else
            {
                var dialog = new FetchOptionsDialog(_config.DefaultDownloadFolder, _config.StoreFolder, _config.KeepStoreByDefault);
                if (dialog.ShowDialog() != true) return; // cancelled - link stays typed, nothing starts
                reconstructFolder = dialog.ReconstructFolder;
                storeFolder = dialog.StoreFolder;
                keepStore = dialog.KeepStore;
            }

            _taskManager.StartFetch(DownloadLinkInput, reconstructFolder, storeFolder, keepStore, useFec: false);
            DownloadLinkInput = "";
        });

        StartPackCommand = new RelayCommand(_ =>
        {
            if (string.IsNullOrWhiteSpace(PackInputPath)) return;
            _taskManager.StartPack(PackInputPath, null, _config.StoreFolder, noCopy: false);
            PackInputPath = "";
        });

        StartServeCommand = new RelayCommand(_ =>
        {
            if (string.IsNullOrWhiteSpace(ServeFilePath)) return;
            _taskManager.StartServe(ServeFilePath, _config.StoreFolder, _config.DefaultListenAddress, _config.TrackerUrl);
            ServeFilePath = "";
        });

        StopOperationCommand = new RelayCommand(async param =>
        {
            try
            {
                if (param is string id) await _taskManager.StopAsync(id, TimeSpan.FromSeconds(5));
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    $"Failed to stop operation:\n{ex.Message}",
                    "np2ptp-gui", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        });

        RetryOperationCommand = new RelayCommand(param =>
        {
            if (param is string id) _taskManager.Retry(id);
        });

        BrowsePackFileCommand = new RelayCommand(_ =>
        {
            var dialog = new Microsoft.Win32.OpenFileDialog();
            if (dialog.ShowDialog() == true) PackInputPath = dialog.FileName;
        });

        BrowsePackFolderCommand = new RelayCommand(_ =>
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog();
            if (dialog.ShowDialog() == true) PackInputPath = dialog.FolderName;
        });
    }
}
