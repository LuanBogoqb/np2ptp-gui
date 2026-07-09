namespace Np2ptpGui;

using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;

public partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();

    private void MainTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not TabControl { SelectedItem: TabItem { Content: UIElement content } }) return;
        content.Opacity = 0;
        content.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
    }
}
