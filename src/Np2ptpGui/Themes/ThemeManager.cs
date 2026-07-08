namespace Np2ptpGui.Themes;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

public static class ThemeManager
{
    private static readonly Uri StylesUri = new("/Np2ptpGui;component/Themes/XpControlStyles.xaml", UriKind.Relative);
    private static readonly Uri LightColorsUri = new("/Np2ptpGui;component/Themes/XpColors.Light.xaml", UriKind.Relative);
    private static readonly Uri DarkColorsUri = new("/Np2ptpGui;component/Themes/XpColors.Dark.xaml", UriKind.Relative);

    private static readonly List<FrameworkElement> Roots = new();
    private static bool _isLight = true;

    public static void ApplyTheme(bool isLight)
    {
        _isLight = isLight;
        var colorsUri = isLight ? LightColorsUri : DarkColorsUri;
        foreach (var root in Roots.ToList())
        {
            root.Resources.MergedDictionaries[1] = new ResourceDictionary { Source = colorsUri };
        }
    }

    public static void Register(FrameworkElement root)
    {
        root.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = StylesUri });
        root.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = _isLight ? LightColorsUri : DarkColorsUri });
        Roots.Add(root);

        if (root is Window window)
        {
            window.Closed += (_, _) => Roots.Remove(root);
        }
    }
}
