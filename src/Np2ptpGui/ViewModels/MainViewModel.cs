namespace Np2ptpGui.ViewModels;

using System;
using System.ComponentModel;
using System.Windows.Data;
using Np2ptpGui.Models;
using Np2ptpGui.Services;

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
            _taskManager.StartFetch(DownloadLinkInput, _config.DefaultDownloadFolder, useFec: false);
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
            if (param is string id) await _taskManager.StopAsync(id, TimeSpan.FromSeconds(5));
        });

        RetryOperationCommand = new RelayCommand(param =>
        {
            if (param is string id) _taskManager.Retry(id);
        });
    }
}
