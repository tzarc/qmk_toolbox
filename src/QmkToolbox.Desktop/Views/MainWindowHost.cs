using Avalonia;
using Avalonia.Controls;
using QmkToolbox.Desktop.Models;
using QmkToolbox.Desktop.Services;
using QmkToolbox.Desktop.ViewModels;

namespace QmkToolbox.Desktop.Views;

/// <summary>
/// Owns the main window's lifecycle wiring: placement restore, native menu, service
/// attachment, session start, and first-start setup on open; settings capture and session
/// stop on close. The code-behind keeps only view-local input handling.
/// </summary>
internal static class MainWindowHost
{
    public static void Attach(MainWindow window, MainWindowViewModel vm, DesktopWindowService windowService)
    {
        window.Opened += async (_, _) =>
        {
            (Size? size, PixelPoint? position) = vm.RestoredBounds(window.Screens.All.Select(s => s.WorkingArea));
            if (size is { } s)
            {
                window.Width = s.Width;
                window.Height = s.Height;
            }
            if (position is { } pos)
                window.Position = pos;

            NativeMenu.SetMenu(window, AppMenu.Build(vm));
            windowService.AttachWindow(window);
            vm.Session.Start();
            await vm.RunFirstStartSetupAsync();
        };

        window.Closing += (_, e) =>
        {
            if (e.Cancel)
                return;
            vm.SaveSettings(new WindowBounds(window.Position.X, window.Position.Y, window.Width, window.Height));
            vm.Session.Stop();
        };
    }
}
