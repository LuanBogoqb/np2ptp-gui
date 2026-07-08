namespace Np2ptpGui.Tests.Services;

using Np2ptpGui.Services;
using Xunit;

public class WindowsThemeServiceTests
{
    [Fact]
    public void IsLightTheme_WhenRegistryValueIsOne_ReturnsTrue()
    {
        var service = new WindowsThemeService(() => 1);

        Assert.True(service.IsLightTheme());
    }

    [Fact]
    public void IsLightTheme_WhenRegistryValueIsZero_ReturnsFalse()
    {
        var service = new WindowsThemeService(() => 0);

        Assert.False(service.IsLightTheme());
    }

    [Fact]
    public void IsLightTheme_WhenRegistryValueIsMissing_ReturnsTrue()
    {
        var service = new WindowsThemeService(() => null);

        Assert.True(service.IsLightTheme());
    }

    [Fact]
    public void IsLightTheme_WhenReaderThrows_ReturnsTrue()
    {
        var service = new WindowsThemeService(() => throw new System.InvalidOperationException("boom"));

        Assert.True(service.IsLightTheme());
    }
}
