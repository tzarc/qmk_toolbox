using Avalonia.Controls;
using QmkToolbox.Desktop.Services;
using QmkToolbox.Desktop.ViewModels;

namespace QmkToolbox.Desktop.Views;

/// <summary>
/// Owns the main window's lifecycle wiring: native menu, service attachment, session start,
/// and first-start setup on open; settings save and session stop on close.
/// <see cref="WindowPlacementHost"/> owns placement, and the code-behind keeps only view-local
/// input handling.
/// </summary>
internal static class MainWindowHost
{
    public static void Attach(MainWindow window, MainWindowViewModel vm, DesktopWindowService windowService)
    {
        WindowPlacementHost.Track(window, vm.Settings);
        window.Opened += (_, _) => OnOpened(window, vm, windowService);

        window.Closing += (_, e) =>
        {
            if (e.Cancel)
                return;
            vm.SaveSettings();
            vm.Session.Stop();
        };
    }

    // Async void so this keeps event-handler crash semantics: a fault here must surface,
    // not vanish into a discarded task.
#pragma warning disable VSTHRD100 // deliberate async void, see above
    private static async void OnOpened(MainWindow window, MainWindowViewModel vm, DesktopWindowService windowService)
#pragma warning restore VSTHRD100
    {
        NativeMenu.SetMenu(window, AppMenu.Build(vm));
        windowService.AttachWindow(window);
        vm.Session.Start();
        await vm.RunFirstStartSetupAsync();
    }
}
