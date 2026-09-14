using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media.TextFormatting;

namespace QmkToolbox.Desktop.Views.Controls;

internal class HitTestableTextBlock : SelectableTextBlock
{
    // Avalonia style type selectors match the exact runtime type, so the theme's
    // "SelectableTextBlock" style (which supplies SelectionBrush/SelectionForegroundBrush)
    // never reaches this subclass; without keying back to the base type, selection would
    // happen but render invisibly.
    protected override Type StyleKeyOverride => typeof(SelectableTextBlock);

    public new TextLayout? TextLayout => base.TextLayout;

    // When set, called on every pointer press. Return true to consume the press, which stops
    // text selection from starting; false falls through to normal selection.
    public Func<PointerPressedEventArgs, bool>? PointerPressInterceptor { get; set; }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        if (PointerPressInterceptor?.Invoke(e) == true)
        {
            e.Handled = true;
            return;
        }
        base.OnPointerPressed(e);
    }

    // SelectableTextBlock collapses the selection when a right-click release lands outside
    // it, after the context menu has already been requested, so the menu's Copy would find
    // nothing selected. Keep the selection: the menu acts on it wherever the click lands.
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton == MouseButton.Right)
        {
            int start = SelectionStart;
            int end = SelectionEnd;
            base.OnPointerReleased(e);
            SetCurrentValue(SelectionStartProperty, start);
            SetCurrentValue(SelectionEndProperty, end);
            return;
        }
        base.OnPointerReleased(e);
    }
}
