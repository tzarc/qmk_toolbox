using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using Avalonia.Markup.Xaml;
using Qmk.Usb.Discovery;
using QmkToolbox.Desktop.Services;
using QmkToolbox.Desktop.ViewModels;
using QmkToolbox.Desktop.Views;

namespace QmkToolbox.Desktop;

/// <summary>Avalonia application entry point: creates the main window, wires commands, and builds the native app menu.</summary>
public partial class App : Application
{
    // Retained for AppAbout_OnClick, the Click handler wired in App.axaml.
    // The AXAML-declared NativeMenu.Menu is loaded during Initialize() and is the menu
    // item macOS actually makes clickable. Do NOT remove this field or AppAbout_OnClick
    // even if a static analyser reports them as "unread": the AXAML Click binding is
    // the only reference and is invisible to Roslyn's read-detection.
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
            var bootloaderServices = new Core.Bootloader.BootloaderServices(toolProvider)
            {
                SerialPorts = new DesktopSerialPortService(),
                MountPoints = new DesktopMountPointService(),
            };
            var orchestrator = new Core.Services.FlashOrchestrator(bootloaderServices);
            // The HID tracker initialises hidapi on Start() and tears it down on Dispose();
            // one is created per HID console window and disposed when that window closes.
            var windowService = new DesktopWindowService(() => new Services.Hid.HidDeviceTracker());

            // One marshalling sink per stream, owned here: every log/trace producer routes
            // through these, so UI-thread marshalling is guaranteed at the composition root
            // rather than by per-caller discipline. The log sink resolves the ViewModel lazily
            // (it is constructed below); Log routes each message by its type's stream
            // discipline (see MessageType.IsRawStream).
            MainWindowViewModel? mainVm = null;
            void logSink(string message, Core.Models.MessageType type) =>
                Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => mainVm?.Log(message, type));
            void traceSink(string message) =>
                Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => windowService.TraceDebug(message));

            usbDetector.DiagnosticTrace = traceSink;
            orchestrator.DiagnosticTrace = traceSink;
            orchestrator.OutputReceived += logSink;

            // The session receives its UI invoker at construction, so USB events arriving from
            // the moment Start() is called are always marshalled; there is no window in which
            // listeners run without an invoker.
            var session = new FlashSession(
                Avalonia.Threading.Dispatcher.UIThread.InvokeAsync,
                usbDetector,
                orchestrator,
                toolProvider,
                logSink)
            {
                DiagnosticTrace = traceSink,
            };
            // The clipboard delegate closes over the window constructed just below; it is
            // invoked only by copy commands, long after the window exists.
            MainWindow? mainWindow = null;
            var vm = new MainWindowViewModel(
                session, toolProvider, new SettingsService(), windowService, ApplyTheme,
                Avalonia.Threading.Dispatcher.UIThread.InvokeAsync,
                text => mainWindow?.Clipboard?.SetTextAsync(text) ?? Task.CompletedTask,
                filePath);
            mainVm = vm;
            _mainWindowViewModel = vm;
            mainWindow = new MainWindow { DataContext = vm };
            MainWindowHost.Attach(mainWindow, vm, windowService);
            desktop.MainWindow = mainWindow;

            // Serves Windows and Linux; on macOS the NSMenuBar reads NativeMenu.Menu from the
            // Application during Initialize(), before this method runs, so this SetMenu has no
            // effect on the macOS app menu (see AppMenu.BuildApplicationMenu).
            NativeMenu.SetMenu(this, AppMenu.BuildApplicationMenu(vm, OperatingSystem.IsMacOS()));
        }

        base.OnFrameworkInitializationCompleted();
    }

    // The Avalonia adapter for the ViewModel's theme-applier seam: maps the persisted
    // variant name onto the application-wide requested theme.
    private static void ApplyTheme(string variant) =>
        Current!.RequestedThemeVariant = variant switch
        {
            "Light" => Avalonia.Styling.ThemeVariant.Light,
            "Default" => Avalonia.Styling.ThemeVariant.Default,
            _ => Avalonia.Styling.ThemeVariant.Dark,
        };

    // Handler for the "About QMK Toolbox" NativeMenuItem declared in App.axaml.
    // On macOS, the AXAML-declared NativeMenu.Menu is what the NSMenuBar uses for the
    // app menu; the programmatic NativeMenu.SetMenu call above does not replace it.
    // This Click handler is therefore the actual code path for About on macOS.
    // Do NOT remove this method; it looks unreferenced to Roslyn but is called by Avalonia
    // via the AXAML Click="AppAbout_OnClick" binding at runtime.
    private void AppAbout_OnClick(object? sender, EventArgs args) => _mainWindowViewModel?.OpenAboutCommand.Execute(null);
}
