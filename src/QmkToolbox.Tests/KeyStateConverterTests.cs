using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Styling;
using QmkToolbox.Desktop.Converters;
using QmkToolbox.Desktop.Models;
using Xunit;

namespace QmkToolbox.Tests;

public class KeyStateConverterTests
{
    private static object? Convert(IMultiValueConverter converter, KeyState state, ThemeVariant variant) =>
        converter.Convert([state, variant], typeof(IBrush), null, CultureInfo.InvariantCulture);

    [Fact]
    public void PressedAndTested_UseTheSameHighlightInBothThemes()
    {
        Assert.Same(
            Convert(KeyStateConverters.Background, KeyState.Pressed, ThemeVariant.Dark),
            Convert(KeyStateConverters.Background, KeyState.Pressed, ThemeVariant.Light));
        Assert.Same(
            Convert(KeyStateConverters.Background, KeyState.Tested, ThemeVariant.Dark),
            Convert(KeyStateConverters.Background, KeyState.Tested, ThemeVariant.Light));
    }

    [Fact]
    public void UntestedKeyBackground_FollowsTheTheme()
    {
        var dark = (ISolidColorBrush)Convert(KeyStateConverters.Background, KeyState.Default, ThemeVariant.Dark)!;
        var light = (ISolidColorBrush)Convert(KeyStateConverters.Background, KeyState.Default, ThemeVariant.Light)!;

        Assert.NotEqual(dark.Color, light.Color);
    }

    [Fact]
    public void UntestedKeyLabel_IsWhiteOnDarkAndBlackOnLight()
    {
        Assert.Same(Brushes.White, Convert(KeyStateConverters.Foreground, KeyState.Default, ThemeVariant.Dark));
        Assert.Same(Brushes.Black, Convert(KeyStateConverters.Foreground, KeyState.Default, ThemeVariant.Light));
    }

    // While bindings initialise, inputs can arrive unset; the converter must not throw.
    [Fact]
    public void IncompleteInputs_LeaveThePropertyUnset()
    {
        object? result = KeyStateConverters.Background.Convert(
            [AvaloniaProperty.UnsetValue, AvaloniaProperty.UnsetValue], typeof(IBrush), null, CultureInfo.InvariantCulture);

        Assert.Same(AvaloniaProperty.UnsetValue, result);
    }
}
