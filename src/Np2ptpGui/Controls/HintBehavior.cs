namespace Np2ptpGui.Controls;

using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

public static class HintBehavior
{
    public static readonly DependencyProperty HintProperty =
        DependencyProperty.RegisterAttached(
            "Hint", typeof(string), typeof(HintBehavior),
            new PropertyMetadata("", OnHintChanged));

    public static string GetHint(DependencyObject element) => (string)element.GetValue(HintProperty);
    public static void SetHint(DependencyObject element, string value) => element.SetValue(HintProperty, value);

    private static void OnHintChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBox textBox) return;
        textBox.TextChanged -= OnTextChanged;
        textBox.TextChanged += OnTextChanged;
        UpdateWatermark(textBox);
    }

    private static void OnTextChanged(object sender, TextChangedEventArgs e) => UpdateWatermark((TextBox)sender);

    private static void UpdateWatermark(TextBox textBox)
    {
        var hint = GetHint(textBox);
        if (string.IsNullOrEmpty(textBox.Text) && !string.IsNullOrEmpty(hint))
        {
            textBox.Background = new VisualBrush
            {
                AlignmentX = AlignmentX.Left,
                AlignmentY = AlignmentY.Center,
                Stretch = Stretch.None,
                Visual = new TextBlock { Text = hint, Foreground = Brushes.Gray, Margin = new Thickness(4, 0, 0, 0) },
            };
        }
        else
        {
            textBox.ClearValue(TextBox.BackgroundProperty);
        }
    }
}
