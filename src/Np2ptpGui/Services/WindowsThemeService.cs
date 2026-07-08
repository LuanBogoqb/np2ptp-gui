namespace Np2ptpGui.Services;

using System;
using Microsoft.Win32;

public sealed class WindowsThemeService
{
    private const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string ValueName = "AppsUseLightTheme";

    private readonly Func<int?> _registryReader;
    private bool _lastIsLight;

    public event Action<bool>? ThemeChanged;

    public WindowsThemeService() : this(ReadRegistryValue)
    {
    }

    internal WindowsThemeService(Func<int?> registryReader)
    {
        _registryReader = registryReader;
        _lastIsLight = IsLightTheme();
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    public bool IsLightTheme()
    {
        int? value;
        try
        {
            value = _registryReader();
        }
        catch
        {
            value = null;
        }

        return value != 0;
    }

    private void OnUserPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category != UserPreferenceCategory.General) return;

        var isLight = IsLightTheme();
        if (isLight == _lastIsLight) return;

        _lastIsLight = isLight;
        ThemeChanged?.Invoke(isLight);
    }

    private static int? ReadRegistryValue()
    {
        using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
        var value = key?.GetValue(ValueName);
        return value is int intValue ? intValue : null;
    }
}
