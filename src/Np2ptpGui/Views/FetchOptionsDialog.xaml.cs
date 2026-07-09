namespace Np2ptpGui.Views;

using System.Windows;

public partial class FetchOptionsDialog : Window
{
    public string ReconstructFolder { get; private set; } = "";
    public string StoreFolder { get; private set; } = "";
    public bool KeepStore { get; private set; }

    public FetchOptionsDialog(string defaultReconstructFolder, string defaultStoreFolder, bool defaultKeepStore)
    {
        InitializeComponent();
        ReconstructFolderBox.Text = defaultReconstructFolder;
        StoreFolderBox.Text = defaultStoreFolder;
        KeepStoreCheckBox.IsChecked = defaultKeepStore;
    }

    private void BrowseReconstructFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog();
        if (dialog.ShowDialog(this) == true) ReconstructFolderBox.Text = dialog.FolderName;
    }

    private void BrowseStoreFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog();
        if (dialog.ShowDialog(this) == true) StoreFolderBox.Text = dialog.FolderName;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        ReconstructFolder = ReconstructFolderBox.Text;
        StoreFolder = StoreFolderBox.Text;
        KeepStore = KeepStoreCheckBox.IsChecked == true;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
