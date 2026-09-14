using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using QmkToolbox.Desktop.Models;
using QmkToolbox.Desktop.Services.Hid;
using QmkToolbox.Desktop.ViewModels;
using QmkToolbox.Desktop.Views;

namespace QmkToolbox.Desktop.Services;

public sealed class DesktopWindowService(
    Func<IHidListener> hidListenerFactory, DebugLogViewModel debugLog, SettingsService settings) : IWindowService
{
    private readonly Dictionary<Type, Window> _singletons = [];

    /// <summary>
    /// Binds the service to its owner window. The composition root constructs the service before
    /// any window exists, but every window-facing member runs from UI inside the owner window, so
    /// none can run before attachment.
    /// </summary>
    public void AttachWindow(Window owner)
    {
        Owner = owner;
        owner.Closed += (_, _) =>
        {
            foreach (Window w in _singletons.Values.ToList())
                w.Close();
        };
    }

    private Window Owner { get => field ?? throw new InvalidOperationException("AttachWindow has not been called."); set; }

    public async Task<string?> PickFirmwareFileAsync()
    {
        IReadOnlyList<IStorageFile> files = await Owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open Firmware File",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Firmware Files") { Patterns = FirmwareFiles.PickerPatterns },
                new FilePickerFileType("All Files") { Patterns = ["*"] }
            ]
        });
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    private void ShowSingleton<T>(Func<T> create) where T : Window
    {
        if (_singletons.TryGetValue(typeof(T), out Window? existing))
        {
            existing.Activate();
            return;
        }
        T window = create();
        _singletons[typeof(T)] = window;
        window.Closed += (_, _) => _singletons.Remove(typeof(T));
        WindowPlacementHost.Track(window, settings);
        window.Show(Owner);
    }

    public void ShowKeyTester() =>
        ShowSingleton(() => new KeyTesterWindow { DataContext = new KeyTesterViewModel() });

    // The console window scopes the listener's lifecycle: created here, then disposed by
    // HidConsoleWindow.OnClosed through HidConsoleViewModel.Dispose.
    public void ShowHidConsole() =>
        ShowSingleton(() =>
        {
            var window = new HidConsoleWindow();
            window.DataContext = new HidConsoleViewModel(
                hidListenerFactory(), Avalonia.Threading.Dispatcher.UIThread.InvokeAsync, ClipboardOf(window));
            return window;
        });

    public void ShowAbout()
    {
        var win = new AboutWindow();
        _ = win.ShowDialog(Owner);
    }

    public void ShowDebugLog() =>
        ShowSingleton(() => new DebugLogWindow { DataContext = debugLog });

    // Lazy: copy commands resolve the clipboard when they run, long after the window exists.
    private static Func<string, Task> ClipboardOf(Window window) =>
        text => window.Clipboard?.SetTextAsync(text) ?? Task.CompletedTask;
}
