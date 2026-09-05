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
            var bootloaderServices = new Core.Bootloader.BootloaderServices(toolProvider)
            {
                SerialPorts = new DesktopSerialPortService(),
                MountPoints = new DesktopMountPointService(),
            };
            var orchestrator = new Core.Services.FlashOrchestrator(bootloaderServices);
            // The HID tracker initialises hidapi on Start() and tears it down on Dispose();
            // each HID console window creates its own and disposes it on close.
            var windowService = new DesktopWindowService(() => new Services.Hid.HidDeviceTracker());

            // Every log/trace producer routes through these two sinks, which marshal onto
            // the UI thread; no caller marshals for itself. The log sink resolves the
            // ViewModel lazily because it is constructed below.
            MainWindowViewModel? mainVm = null;
            void logSink(string message, Core.Models.MessageType type) =>
                Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => mainVm?.Log(message, type));
            void traceSink(string message) =>
                Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => windowService.TraceDebug(message));

            usbDetector.DiagnosticTrace = traceSink;
            orchestrator.DiagnosticTrace = traceSink;
            orchestrator.OutputReceived += logSink;

            // The session takes its UI invoker at construction, so USB events are marshalled
            // from the moment Start() is called.
            var session = new FlashSession(
                Avalonia.Threading.Dispatcher.UIThread.InvokeAsync,
                usbDetector,
                orchestrator,
                toolProvider,
                logSink)
            {
                DiagnosticTrace = traceSink,
            };
            // The clipboard delegate closes over the window constructed below; copy commands
            // invoke it long after the window exists.
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
