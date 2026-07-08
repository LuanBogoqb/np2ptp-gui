namespace Np2ptpGui.Controls;

using System.Windows;

public static class HintBehavior
{
    public static readonly DependencyProperty HintProperty =
        DependencyProperty.RegisterAttached(
            "Hint", typeof(string), typeof(HintBehavior), new PropertyMetadata(""));

    public static string GetHint(DependencyObject element) => (string)element.GetValue(HintProperty);
    public static void SetHint(DependencyObject element, string value) => element.SetValue(HintProperty, value);
}
