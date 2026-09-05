using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Styling;
using QmkToolbox.Desktop.Models;

namespace QmkToolbox.Desktop.Converters;

/// <summary>
/// Brush lookups for key-tester keys, keyed on key state and theme. Immutable brushes are
/// resolved once; colour values stay out of XAML.
/// </summary>
internal static class KeyStateStyles
{
    private static readonly IBrush DarkKey = new ImmutableSolidColorBrush(Color.Parse("#3A3A3A"));
    private static readonly IBrush LightKey = new ImmutableSolidColorBrush(Color.Parse("#D8D8D8"));

    internal static IBrush GetBackground(KeyState state, bool isDark) => state switch
    {
        KeyState.Default => isDark ? DarkKey : LightKey,
        KeyState.Pressed => Brushes.Yellow,
        KeyState.Tested => Brushes.Lime,
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, null),
    };

    // Pressed and tested backgrounds are bright in both themes, so their labels are always
    // black; only an untested key's label follows the theme.
    internal static IBrush GetForeground(KeyState state, bool isDark) =>
        state == KeyState.Default && isDark ? Brushes.White : Brushes.Black;
}

/// <summary>
/// Multi-value converters that combine a key's <see cref="KeyState"/> with the window's actual
/// theme variant, so key colours update when either changes.
/// </summary>
public static class KeyStateConverters
{
    public static readonly IMultiValueConverter Background =
        new KeyStateBrushConverter(KeyStateStyles.GetBackground);

    public static readonly IMultiValueConverter Foreground =
        new KeyStateBrushConverter(KeyStateStyles.GetForeground);

    private sealed class KeyStateBrushConverter(Func<KeyState, bool, IBrush> brush) : IMultiValueConverter
    {
        public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture) =>
            values is [KeyState state, ThemeVariant variant, ..]
                ? brush(state, variant == ThemeVariant.Dark)
                : AvaloniaProperty.UnsetValue;
    }
}
