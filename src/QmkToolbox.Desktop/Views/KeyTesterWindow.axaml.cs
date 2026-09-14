using Avalonia.Controls;
using Avalonia.Input;
using QmkToolbox.Desktop.ViewModels;

namespace QmkToolbox.Desktop.Views;

/// <summary>
/// Key tester window: forwards physical key events to the ViewModel. Layout and colours are
/// declared in AXAML over the ViewModel's key table.
/// </summary>
public partial class KeyTesterWindow : Window
{
    public KeyTesterWindow()
    {
        InitializeComponent();
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        Focus();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (DataContext is KeyTesterViewModel vm)
            vm.OnKeyDown(e.PhysicalKey);
        e.Handled = true;
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        if (DataContext is KeyTesterViewModel vm)
            vm.OnKeyUp(e.PhysicalKey);
        e.Handled = true;
    }
}
