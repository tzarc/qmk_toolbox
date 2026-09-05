using Avalonia;
using NSubstitute;
using Qmk.Usb.Discovery;
using QmkToolbox.Core.Bootloader;
using QmkToolbox.Core.Services;
using QmkToolbox.Desktop.Models;
using QmkToolbox.Desktop.Services;
using QmkToolbox.Desktop.ViewModels;
using Xunit;

namespace QmkToolbox.Tests;

/// <summary>
/// Drives MainWindowViewModel through its constructor seams: a substitute
/// <see cref="IWindowService"/> and a recording theme applier stand in for Avalonia, the real
/// FlashSession runs over the usual fakes, and settings live in a per-test temp file.
/// </summary>
public sealed class MainWindowViewModelTests : IDisposable
{
    private sealed class FakeUsbDetector : IUsbEventsDetector
    {
        public event Action<UsbDeviceInfo>? DeviceConnected;
        public event Action<UsbDeviceInfo>? DeviceDisconnected;
        public Action<string>? DiagnosticTrace { get; set; }

        public void Start() { }
        public void Stop() { }
        public void Dispose() { }

        public void RaiseConnected(UsbDeviceInfo device) => DeviceConnected?.Invoke(device);
        public void RaiseDisconnected(UsbDeviceInfo device) => DeviceDisconnected?.Invoke(device);
    }

    private readonly FakeUsbDetector _detector = new();
    private readonly IWindowService _windowService = Substitute.For<IWindowService>();
    private readonly List<string> _appliedThemes = [];
    private readonly IFlashToolProvider _toolProvider;
    private readonly FlashSession _session;
    private readonly SettingsService _settings;
    private readonly string _settingsPath;

    public MainWindowViewModelTests()
    {
        _settingsPath = Path.Combine(Path.GetTempPath(), $"mwvm-test-{Guid.NewGuid():N}.json");
        _settings = new SettingsService(_settingsPath);
        _toolProvider = Substitute.For<IFlashToolProvider>();
        var orchestrator = new FlashOrchestrator(new BootloaderServices(_toolProvider)
        {
            ProcessRunner = new CapturingProcessRunner(),
            SerialPorts = Substitute.For<ISerialPortService>(),
            MountPoints = Substitute.For<IMountPointService>(),
        });
        _session = new FlashSession(f => f(), _detector, orchestrator, _toolProvider, (_, _) => { });
    }

    public void Dispose()
    {
        if (File.Exists(_settingsPath))
            File.Delete(_settingsPath);
    }

    private MainWindowViewModel NewVm(string filePath = "") =>
        new(_session, _toolProvider, _settings, _windowService, _appliedThemes.Add, f => f(), _ => Task.CompletedTask, filePath);

    // ── theme ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Construction_AppliesPersistedThemeVariant()
    {
        _settings.Current.ThemeVariant = "Dark";

        MainWindowViewModel vm = NewVm();

        Assert.Equal("Dark", Assert.Single(_appliedThemes));
        Assert.True(vm.IsDarkTheme);
    }

    [Fact]
    public void SetTheme_AppliesVariantAndUpdatesFlags()
    {
        MainWindowViewModel vm = NewVm();

        vm.SetThemeCommand.Execute("Light");

        Assert.Equal("Light", _appliedThemes[^1]);
        Assert.True(vm.IsLightTheme);
        Assert.False(vm.IsDarkTheme);
    }

    // ── startup banner ────────────────────────────────────────────────────────

    [Fact]
    public void Construction_LogsStartupBanner()
    {
        MainWindowViewModel vm = NewVm();

        string log = TerminalProjection.ToText(vm.Buffer);
        Assert.Contains("QMK Toolbox", log);
        Assert.Contains("Supported bootloaders:", log);
        Assert.Contains("via dfu-util", log);
        Assert.Contains("Supported ISP flashers:", log);
    }

    // ── firmware path ─────────────────────────────────────────────────────────

    [Fact]
    public void Construction_WithFilePathArgument_SelectsFirmware()
    {
        NewVm("/fw/from-cli.hex");

        Assert.Equal("/fw/from-cli.hex", _session.FirmwarePath);
    }

    [Fact]
    public async Task OpenFile_PickedFile_SelectsFirmware()
    {
        _windowService.PickFirmwareFileAsync().Returns("/fw/picked.hex");
        MainWindowViewModel vm = NewVm();

        await vm.OpenFileCommand.ExecuteAsync(null);

        Assert.Equal("/fw/picked.hex", _session.FirmwarePath);
    }

    [Fact]
    public async Task OpenFile_PickerCancelled_KeepsCurrentFirmware()
    {
        _windowService.PickFirmwareFileAsync().Returns((string?)null);
        MainWindowViewModel vm = NewVm();

        await vm.OpenFileCommand.ExecuteAsync(null);

        Assert.Equal("", _session.FirmwarePath);
    }

    // ── command enablement relay ──────────────────────────────────────────────

    [Fact]
    public void DeviceConnected_EnablesFlashCommand()
    {
        MainWindowViewModel vm = NewVm();
        bool raised = false;
        vm.FlashCommand.CanExecuteChanged += (_, _) => raised = true;
        Assert.False(vm.FlashCommand.CanExecute(null));

        _detector.RaiseConnected(new UsbDeviceInfo(0x03EB, 0x2FEF, 0, "", "", "", ""));

        Assert.True(raised);
        Assert.True(vm.FlashCommand.CanExecute(null));
    }

    // ── first-start setup ─────────────────────────────────────────────────────

    [FactOnLinux] // RunFirstStartSetupAsync's confirm prompt is OS-specific (udev rules on Linux)
    public async Task FirstStart_ConfirmDeclined_CompletesAndClearsFirstStartFlag()
    {
        _settings.Current.FirstStart = true;
        MainWindowViewModel vm = NewVm();

        Task setup = vm.RunFirstStartSetupAsync();
        Assert.True(vm.IsConfirmVisible);
        Assert.Equal("Linux udev Rules", vm.ConfirmTitle);

        vm.ConfirmNoCommand.Execute(null);
        await setup;

        Assert.False(vm.IsConfirmVisible);
        Assert.False(_settings.Current.FirstStart);
    }

    // ── window bounds ─────────────────────────────────────────────────────────

    private static readonly PixelRect Screen = new(0, 0, 1920, 1080);

    [Fact]
    public void SaveSettings_PersistsWindowBounds_AcrossReload()
    {
        NewVm().SaveSettings(new WindowBounds(100, 200, 800, 600));

        var reloaded = new SettingsService(_settingsPath);
        var vm = new MainWindowViewModel(_session, _toolProvider, reloaded, _windowService, _appliedThemes.Add, f => f(), _ => Task.CompletedTask);
        (Size? size, PixelPoint? position) = vm.RestoredBounds([Screen]);

        Assert.Equal(new Size(800, 600), size);
        Assert.Equal(new PixelPoint(100, 200), position);
    }

    // A window saved on a monitor that has since been unplugged keeps its size but falls
    // back to the default position.
    [Fact]
    public void RestoredBounds_OffScreenPosition_DropsPositionAndKeepsSize()
    {
        NewVm().SaveSettings(new WindowBounds(5000, 5000, 800, 600));

        (Size? size, PixelPoint? position) = NewVm().RestoredBounds([Screen]);

        Assert.Equal(new Size(800, 600), size);
        Assert.Null(position);
    }

    [Fact]
    public void RestoredBounds_FirstRun_ReturnsNothing()
    {
        (Size? size, PixelPoint? position) = NewVm().RestoredBounds([Screen]);

        Assert.Null(size);
        Assert.Null(position);
    }
}
