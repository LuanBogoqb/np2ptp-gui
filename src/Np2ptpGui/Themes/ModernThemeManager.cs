namespace Np2ptpGui.Themes;

using System.Windows;
using System.Windows.Controls;
using Wpf.Ui.Appearance;
using Wpf.Ui.Markup;

public static class ModernThemeManager
{
    public const string FamilyName = "Modern";

    public static void Initialize(bool isLight)
    {
        var merged = Application.Current.Resources.MergedDictionaries;
        merged.Add(new ControlsDictionary());
        merged.Add(new ThemesDictionary { Theme = isLight ? ApplicationTheme.Light : ApplicationTheme.Dark });
        ApplicationAccentColorManager.ApplySystemAccent();
    }

    // Matches Np2ptpGui.Themes.ThemeManager.ApplyTheme's own pattern: ApplicationThemeManager.Apply
    // mutating the same ThemesDictionary instance in place doesn't reliably re-render already-
    // applied DynamicResource consumers (confirmed empirically) - remove/re-insert a fresh instance
    // instead, at the same index Initialize put it (1: after ControlsDictionary at 0).
    public static void ApplyTheme(bool isLight)
    {
        var merged = Application.Current.Resources.MergedDictionaries;
        merged.RemoveAt(1);
        merged.Insert(1, new ThemesDictionary { Theme = isLight ? ApplicationTheme.Light : ApplicationTheme.Dark });
    }

    // A plain Window has no implicit style of its own, so WPF-UI's theme-aware
    // Background/Foreground never reach it automatically the way they reach
    // Button/TextBox/etc. (which DO have implicit styles) - reference the same
    // semantic brush keys WPF-UI's own FluentWindow template uses internally.
    public static void ApplyToWindow(Window window)
    {
        window.SetResourceReference(Control.BackgroundProperty, "ApplicationBackgroundBrush");
        window.SetResourceReference(Control.ForegroundProperty, "TextFillColorPrimaryBrush");
    }
}
