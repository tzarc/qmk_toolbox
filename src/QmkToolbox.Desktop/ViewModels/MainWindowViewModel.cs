using System.ComponentModel;
using System.Reflection;
using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QmkToolbox.Core.Bootloader;
using QmkToolbox.Core.Models;
using QmkToolbox.Core.Services;
using QmkToolbox.Desktop.Models;
using QmkToolbox.Desktop.Services;

namespace QmkToolbox.Desktop.ViewModels;

/// <summary>
/// Thin adapter binding Avalonia to the <see cref="FlashSession"/>: commands, theme switching,
/// the confirm-dialog protocol, and startup logging. Flash-domain state and policy live on the
/// session; XAML binds to it via <see cref="Session"/>.
/// </summary>
public partial class MainWindowViewModel : LogViewModelBase
{
    [ObservableProperty] private string _themeVariant = "Default";

    [ObservableProperty] private bool _isConfirmVisible;
    [ObservableProperty] private string _confirmTitle = "";
    [ObservableProperty] private string _confirmMessage = "";
    private TaskCompletionSource<bool>? _confirmTcs;

    public bool IsWindows { get; }
    public bool IsLinux { get; }

    public FlashSession Session { get; }
    public SettingsService Settings { get; }

    private readonly IFlashToolProvider _toolProvider;
    private readonly IWindowService _windowService;
    private readonly Action<string> _themeApplier;

    public MainWindowViewModel(
        FlashSession session,
        IFlashToolProvider toolProvider,
        SettingsService settingsService,
        IWindowService windowService,
        Action<string> themeApplier,
        Func<Func<Task>, Task> uiInvoker,
        Func<string, Task> setClipboardText,
        string filePath = "",
        bool? isWindows = null,
        bool? isLinux = null)
        : base(uiInvoker, setClipboardText)
    {
        IsWindows = isWindows ?? OperatingSystem.IsWindows();
        IsLinux = isLinux ?? OperatingSystem.IsLinux();
        Session = session;
        _toolProvider = toolProvider;
        _windowService = windowService;
        _themeApplier = themeApplier;
        Settings = settingsService;
        Settings.ErrorLogger = LogError;

        Session.PropertyChanged += OnSessionPropertyChanged;

        ThemeVariant = Settings.Current.ThemeVariant;
        Session.LoadFrom(Settings.Current);
        LogStartupBanner();

        if (!string.IsNullOrEmpty(filePath))
            Session.SetFirmwarePath(filePath);
    }

    private void OnSessionPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        switch (args.PropertyName)
        {
            case nameof(FlashSession.CanFlash):
                FlashCommand.NotifyCanExecuteChanged();
                break;
            case nameof(FlashSession.CanReset):
                ResetCommand.NotifyCanExecuteChanged();
                break;
            case nameof(FlashSession.CanClearEeprom):
                ClearEepromCommand.NotifyCanExecuteChanged();
                SetLeftHandCommand.NotifyCanExecuteChanged();
                SetRightHandCommand.NotifyCanExecuteChanged();
                break;
            case nameof(FlashSession.CanClearResources):
                ClearResourcesCommand.NotifyCanExecuteChanged();
                break;
        }
    }

    partial void OnThemeVariantChanged(string value)
    {
        _themeApplier(value);
        OnPropertyChanged(nameof(IsDarkTheme));
        OnPropertyChanged(nameof(IsLightTheme));
        OnPropertyChanged(nameof(IsSystemTheme));
    }

    public bool IsDarkTheme => ThemeVariant == "Dark";
    public bool IsLightTheme => ThemeVariant == "Light";
    public bool IsSystemTheme => ThemeVariant == "Default";

    [RelayCommand]
    private void SetTheme(string variant) => ThemeVariant = variant;

    public async Task RunFirstStartSetupAsync()
    {
        if (!Settings.Current.FirstStart)
            return;

        if (IsWindows)
        {
            if (await ShowConfirmAsync("Windows Driver Installation", "Would you like to install Windows drivers for QMK-supported bootloaders?"))
                InstallDrivers();
        }
        else if (IsLinux)
        {
            if (await ShowConfirmAsync("Linux udev Rules", "Would you like to install Linux udev rules for QMK-supported bootloaders and HID devices?"))
                await InstallUdevRules();
        }

        Settings.Current.FirstStart = false;
        Settings.Save();
    }

    private Task<bool> ShowConfirmAsync(string title, string message)
    {
        _confirmTcs?.TrySetResult(false);
        ConfirmTitle = title;
        ConfirmMessage = message;
        IsConfirmVisible = true;
        _confirmTcs = new TaskCompletionSource<bool>();
        return _confirmTcs.Task;
    }

    [RelayCommand]
    private void ConfirmYes() => CompleteConfirm(true);

    [RelayCommand]
    private void ConfirmNo() => CompleteConfirm(false);

    private void CompleteConfirm(bool result)
    {
        IsConfirmVisible = false;
        _confirmTcs?.TrySetResult(result);
        _confirmTcs = null;
    }

    /// <summary>
    /// Persists the window bounds passed by the host, the theme, and the session's flash
    /// settings.
    /// </summary>
    public void SaveSettings(WindowBounds bounds)
    {
        AppSettings s = Settings.Current;
        s.WindowX = bounds.X;
        s.WindowY = bounds.Y;
        s.WindowWidth = bounds.Width;
        s.WindowHeight = bounds.Height;
        s.ThemeVariant = ThemeVariant;
        Session.SaveTo(s);
        Settings.Save();
    }

    /// <summary>
    /// The placement to restore: the saved size (null on first run) and the saved position
    /// when it lies within one of the given work areas (null otherwise, e.g. after a monitor
    /// was unplugged).
    /// </summary>
    public (Size? Size, PixelPoint? Position) RestoredBounds(IEnumerable<PixelRect> workAreas)
    {
        AppSettings s = Settings.Current;
        Size? size = s.WindowWidth is { } w && s.WindowHeight is { } h ? new Size(w, h) : null;
        PixelPoint? position = s.WindowX is { } x && s.WindowY is { } y
            ? WindowPlacement.Clamp(new PixelPoint((int)x, (int)y), workAreas)
            : null;
        return (size, position);
    }

    private void LogStartupBanner()
    {
        string version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.1";
        string dirty = ThisAssembly.Git.IsDirty ? "-dirty" : "";
        string gitRev = string.IsNullOrEmpty(ThisAssembly.Git.Tag)
            ? ThisAssembly.Git.Commit + dirty
            : ThisAssembly.Git.Tag + dirty;
        string buildDate = ThisAssembly.Git.CommitDate[..10];
        LogInfo($"QMK Toolbox {version} ({gitRev}, {buildDate}) (https://qmk.fm/toolbox)");
        LogInfo(_toolProvider.DescribeVersions());

        LogInfo("Supported bootloaders:");
        foreach (BootloaderBanner.Entry entry in BootloaderBanner.Bootloaders)
            LogInfo($" - {entry.Line}");
        LogInfo("Supported ISP flashers:");
        foreach (BootloaderBanner.Entry entry in BootloaderBanner.IspFlashers)
            LogInfo($" - {entry.Line}");
    }

    private bool CanFlash => Session.CanFlash;
    private bool CanReset => Session.CanReset;
    private bool CanClearEeprom => Session.CanClearEeprom;
    private bool CanClearResources => Session.CanClearResources;

    [RelayCommand(CanExecute = nameof(CanFlash))]
    private Task Flash() => Session.FlashAsync();

    [RelayCommand(CanExecute = nameof(CanReset))]
    private Task Reset() => Session.ResetAsync();

    [RelayCommand(CanExecute = nameof(CanClearEeprom))]
    private Task ClearEeprom() => Session.ClearEepromAsync();

    [RelayCommand(CanExecute = nameof(CanClearEeprom))]
    private Task SetLeftHand() => Session.SetHandednessAsync(left: true);

    [RelayCommand(CanExecute = nameof(CanClearEeprom))]
    private Task SetRightHand() => Session.SetHandednessAsync(left: false);

    [RelayCommand(CanExecute = nameof(CanClearResources))]
    private Task ClearResources() => Session.ClearResourcesAsync();

    [RelayCommand]
    private void Exit()
    {
        if (Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime lt)
            lt.Shutdown();
    }

    [RelayCommand]
    private async Task OpenFile()
    {
        string? path = await _windowService.PickFirmwareFileAsync();
        if (path != null)
            Session.SetFirmwarePath(path);
    }

    [RelayCommand]
    private void OpenKeyTester() => _windowService.ShowKeyTester();

    [RelayCommand]
    private void OpenHidConsole() => _windowService.ShowHidConsole();

    [RelayCommand]
    private void OpenAbout() => _windowService.ShowAbout();

    [RelayCommand]
    private void OpenDebugLog() => _windowService.ShowDebugLog();

    [RelayCommand]
    private void InstallDrivers() => WindowsDriversInstaller.Install(_toolProvider, LogError);

    [RelayCommand]
    private async Task InstallUdevRules() =>
        await LinuxUdevInstaller.InstallAsync(
            _toolProvider,
            msg => Invoke(() => Log(msg, MessageType.UdevOutput)),
            msg => Invoke(() => Log(msg, MessageType.Error)));

    [RelayCommand]
    private void ToggleAutoFlash() => Session.AutoFlashEnabled = !Session.AutoFlashEnabled;

    [RelayCommand]
    private void ToggleShowAllDevices() => Session.ShowAllDevices = !Session.ShowAllDevices;

    public void LogError(string message) => Log(message, MessageType.Error);
    public void LogInfo(string message) => Log(message, MessageType.Info);
}
