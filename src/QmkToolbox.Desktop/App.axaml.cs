using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using Avalonia.Markup.Xaml;
using QmkToolbox.Core.Services;
using QmkToolbox.Desktop.Services;
using QmkToolbox.Desktop.ViewModels;
using QmkToolbox.Desktop.Views;
using QmkToolbox.Usb.Discovery;

namespace QmkToolbox.Desktop;

/// <summary>Composition root: constructs the services and view models, opens the main window, and installs the native menu.</summary>
public partial class App : Application
{
    // Backs AppAbout_OnClick, the Click handler wired in App.axaml. Do not remove this
    // field or the handler even if a static analyser reports them as unread: the AXAML
    // Click binding is their only reference, and Roslyn cannot see it.
    private MainWindowViewModel? _mainWindowViewModel;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            string[] args = desktop.Args ?? [];
            string filePath = args.Length > 0 ? args[0] : "";
            var toolProvider = new FlashToolProvider();
            var usbDetector = new UsbDeviceTracker();

            // The clipboard delegate closes over the window constructed below; copy commands
            // invoke it long after the window exists. Every window writes to the same system
            // clipboard, so the auxiliary windows share the main window's.
            MainWindow? mainWindow = null;
            Task setClipboardTextAsync(string text) => mainWindow?.Clipboard?.SetTextAsync(text) ?? Task.CompletedTask;

            // The Debug Log must accumulate trace from startup, so its ViewModel outlives any
            // window: opening the window shows what already happened, not only what follows.
            var debugLog = new DebugLogViewModel(
                Avalonia.Threading.Dispatcher.UIThread.InvokeAsync, setClipboardTextAsync);

            var settings = new SettingsService();

            // Each HID console window creates its own tracker and disposes it on close.
            var windowService = new DesktopWindowService(
                () => new Services.Hid.HidDeviceTracker(), debugLog, settings);

            // Every producer writes through this one output, which marshals onto the UI thread
            // and dispatches on message type; no caller marshals for itself. The main log
            // resolves its ViewModel lazily because it is constructed below.
            MainWindowViewModel? mainVm = null;
            void output(string message, Core.Models.MessageType type) =>
                _ = Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (type == Core.Models.MessageType.Debug)
                        debugLog.Append(message);
                    else
                        mainVm?.Log(message, type);
                });
            void trace(string message) => output(message, Core.Models.MessageType.Debug);

            usbDetector.DiagnosticTrace = trace;

            var bootloaderServices = new Core.Bootloader.BootloaderServices(
                toolProvider, new UsbDeviceResolver(), output);
            var orchestrator = new FlashOrchestrator(bootloaderServices);

            // The session takes its UI invoker at construction, so USB events are marshalled
            // from the moment Start() is called.
            var session = new FlashSession(
                Avalonia.Threading.Dispatcher.UIThread.InvokeAsync,
                usbDetector,
                orchestrator,
                toolProvider,
                output);
            var vm = new MainWindowViewModel(
                session, toolProvider, settings, windowService, ApplyTheme,
                Avalonia.Threading.Dispatcher.UIThread.InvokeAsync,
                setClipboardTextAsync,
                filePath);
            mainVm = vm;
            _mainWindowViewModel = vm;
            mainWindow = new MainWindow { DataContext = vm };
            MainWindowHost.Attach(mainWindow, vm, windowService);
            desktop.MainWindow = mainWindow;

            // Serves Windows and Linux only: on macOS the NSMenuBar reads NativeMenu.Menu from
            // the Application during Initialize(), before this method runs, so this SetMenu
            // cannot change the macOS app menu (see AppMenu.BuildApplicationMenu).
            NativeMenu.SetMenu(this, AppMenu.BuildApplicationMenu(vm, OperatingSystem.IsMacOS()));
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static void ApplyTheme(string variant) =>
        Current!.RequestedThemeVariant = variant switch
        {
            "Light" => Avalonia.Styling.ThemeVariant.Light,
            "Default" => Avalonia.Styling.ThemeVariant.Default,
            _ => Avalonia.Styling.ThemeVariant.Dark,
        };

    // Click handler for the "About QMK Toolbox" item declared in App.axaml. The NSMenuBar
    // uses that AXAML menu for the macOS app menu, so this is the About code path there.
    // Do not remove: Roslyn sees no reference, but Avalonia calls it at runtime via the
    // AXAML Click binding.
    private void AppAbout_OnClick(object? sender, EventArgs args) => _mainWindowViewModel?.OpenAboutCommand.Execute(null);
}
