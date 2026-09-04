using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using QmkToolbox.Desktop.Models;
using QmkToolbox.Desktop.Services;
using QmkToolbox.Desktop.ViewModels;

namespace QmkToolbox.Desktop.Views;

/// <summary>Main application window: hosts firmware selection, flashing controls, and the log panel.</summary>
public partial class MainWindow : Window
{
    private readonly DesktopWindowService? _windowService;

    // Parameterless overload for the XAML designer/loader only.
    public MainWindow() : this(null) { }

    public MainWindow(DesktopWindowService? windowService)
    {
        _windowService = windowService;
        InitializeComponent();
        AddHandler(DragDrop.DropEvent, OnDrop);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
    }

    protected override async void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        if (DataContext is not MainWindowViewModel vm)
            return;

        AppSettings settings = vm.Settings.Current;
        if (settings.WindowWidth.HasValue && settings.WindowHeight.HasValue)
        {
            Width = settings.WindowWidth.Value;
            Height = settings.WindowHeight.Value;
        }
        if (settings.WindowX.HasValue && settings.WindowY.HasValue)
        {
            var saved = new PixelPoint((int)settings.WindowX.Value, (int)settings.WindowY.Value);
            if (WindowPlacement.Clamp(saved, Screens.All.Select(s => s.WorkingArea)) is { } pos)
                Position = pos;
        }

        NativeMenu.SetMenu(this, AppMenu.Build(vm));

        // The session marshals USB events itself (invoker supplied at construction); this
        // invoker only serves the ViewModel's own background callbacks (e.g. udev install).
        vm.SetUiInvoker(Avalonia.Threading.Dispatcher.UIThread.InvokeAsync);
        _windowService?.AttachWindow(this);
        if (Clipboard is { } clipboard)
            vm.SetClipboardFunc(clipboard.SetTextAsync);
        vm.Session.Start();
        await vm.RunFirstStartSetupAsync();
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            // Save window bounds before vm.SaveSettings() serialises the whole settings object
            AppSettings s = vm.Settings.Current;
            s.WindowX = Position.X;
            s.WindowY = Position.Y;
            s.WindowWidth = Width;
            s.WindowHeight = Height;

            vm.SaveSettings();
            vm.Session.Stop();
        }
        base.OnClosing(e);
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        if (!e.DataTransfer.Contains(DataFormat.File))
        {
            e.DragEffects = DragDropEffects.None;
            return;
        }
        string? path = e.DataTransfer.TryGetFile()?.TryGetLocalPath();
        e.DragEffects = path != null && FirmwareFiles.IsFirmwareFile(path)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm)
            return;
        if (!e.DataTransfer.Contains(DataFormat.File))
            return;

        IStorageItem? file = e.DataTransfer.TryGetFile();
        if (file == null)
            return;

        string? path = file.TryGetLocalPath();
        if (path != null && FirmwareFiles.IsFirmwareFile(path))
            vm.Session.SetFirmwarePath(path);
    }
}
