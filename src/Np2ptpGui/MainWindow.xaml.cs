namespace Np2ptpGui;

using System.Windows;
using Np2ptpGui.Themes;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        ThemeManager.Register(this);
    }
}
