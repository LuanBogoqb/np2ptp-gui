namespace Np2ptpGui.Themes;

using System;
using System.Windows;

public static class ThemeManager
{
    private static readonly Uri StylesUri = new("/Np2ptpGui;component/Themes/XpControlStyles.xaml", UriKind.Relative);
    private static readonly Uri LightColorsUri = new("/Np2ptpGui;component/Themes/XpColors.Light.xaml", UriKind.Relative);
    private static readonly Uri DarkColorsUri = new("/Np2ptpGui;component/Themes/XpColors.Dark.xaml", UriKind.Relative);

    // Merged into Application.Current.Resources (via code, not App.xaml markup) so that:
    // (a) every window/control in the app resolves these DynamicResource keys through the
    //     standard resource-lookup fallback to Application.Resources, with no per-window wiring;
    // (b) swapping the colors dictionary here actually re-renders already-applied
    //     ControlTemplate Setters using DynamicResource - a per-Window MergedDictionaries swap
    //     does not reliably trigger that re-evaluation, confirmed by direct testing.
    public static void Initialize(bool isLight)
    {
        var merged = Application.Current.Resources.MergedDictionaries;
        merged.Add(new ResourceDictionary { Source = StylesUri });
        merged.Add(new ResourceDictionary { Source = isLight ? LightColorsUri : DarkColorsUri });
    }

    public static void ApplyTheme(bool isLight)
    {
        var colorsUri = isLight ? LightColorsUri : DarkColorsUri;
        var merged = Application.Current.Resources.MergedDictionaries;
        merged.RemoveAt(1);
        merged.Insert(1, new ResourceDictionary { Source = colorsUri });
    }
}
