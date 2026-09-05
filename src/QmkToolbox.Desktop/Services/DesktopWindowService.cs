using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using QmkToolbox.Desktop.Models;
using QmkToolbox.Desktop.ViewModels;
using QmkToolbox.Desktop.Views;

namespace QmkToolbox.Desktop.Services;

public sealed class DesktopWindowService(Func<IHidListener> hidListenerFactory) : IWindowService
{
    private readonly Dictionary<Type, Window> _singletons = [];

    /// <summary>
    /// Binds the service to its owner window once it exists; the composition root constructs the
    /// service before any window. Every window-facing member fires from UI inside the owner
    /// window, so none can run before attachment.
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
        window.Show(Owner);
    }

    public void ShowKeyTester() =>
        ShowSingleton(() => new KeyTesterWindow { DataContext = new KeyTesterViewModel() });

    // The console window scopes the listener's lifecycle: created here, disposed when the
    // window closes (via HidConsoleWindow.OnClosed → HidConsoleViewModel.Dispose).
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
        win.ShowDialog(Owner);
    }

    public void ShowDebugLog() =>
        ShowSingleton(() =>
        {
            var window = new DebugLogWindow();
            window.DataContext = new DebugLogViewModel(
                Avalonia.Threading.Dispatcher.UIThread.InvokeAsync, ClipboardOf(window));
            return window;
        });

    // Lazy: copy commands resolve the clipboard when they run, long after the window exists.
    private static Func<string, Task> ClipboardOf(Window window) =>
        text => window.Clipboard?.SetTextAsync(text) ?? Task.CompletedTask;

    /// <summary>Appends a diagnostic trace line to the Debug Log window. No-op when the window is not open.</summary>
    public void TraceDebug(string message)
    {
        if (_singletons.TryGetValue(typeof(DebugLogWindow), out Window? w) && w.DataContext is DebugLogViewModel vm)
            vm.Append(message);
    }
}
