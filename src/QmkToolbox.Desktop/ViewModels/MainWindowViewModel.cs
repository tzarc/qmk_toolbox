using System.Reflection;
using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QmkToolbox.Core.Bootloader;
using QmkToolbox.Core.Models;
using QmkToolbox.Core.Services;
using QmkToolbox.Desktop.Services;

namespace QmkToolbox.Desktop.ViewModels;

/// <summary>
/// Thin adapter binding Avalonia to the <see cref="FlashSession"/>: theme switching, the
/// confirm-dialog protocol, the auxiliary windows, and startup logging. Flash state, policy, and
/// commands all live on the session; XAML binds to them through <see cref="Session"/>.
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
        Settings.Output = Log;

        ThemeVariant = Settings.Current.ThemeVariant;
        Session.LoadFrom(Settings.Current);
        LogStartupBanner();

        if (!string.IsNullOrEmpty(filePath))
            Session.SetFirmwarePath(filePath);
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
            if (await ShowConfirmAsync("Windows Driver Installation", "Install Windows drivers for QMK-supported bootloaders?"))
                InstallDrivers();
        }
        else if (IsLinux)
        {
            if (await ShowConfirmAsync("Linux udev Rules", "Install udev rules for QMK-supported bootloaders and HID devices?"))
                await InstallUdevRulesAsync();
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

    /// <summary>Persists the theme and the session's flash settings.</summary>
    public void SaveSettings()
    {
        Settings.Current.ThemeVariant = ThemeVariant;
        Session.SaveTo(Settings.Current);
        Settings.Save();
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

    [RelayCommand]
    private static void Exit()
    {
        if (Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime lt)
            lt.Shutdown();
    }

    [RelayCommand]
    private async Task OpenFileAsync()
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
    private async Task InstallUdevRulesAsync() =>
        await LinuxUdevInstaller.InstallAsync(
            _toolProvider, (msg, type) => Invoke(() => Log(msg, type)));

    [RelayCommand]
    private void ToggleAutoFlash() => Session.AutoFlashEnabled = !Session.AutoFlashEnabled;

    [RelayCommand]
    private void ToggleShowAllDevices() => Session.ShowAllDevices = !Session.ShowAllDevices;

    public void LogError(string message) => Log(message, MessageType.Error);
    public void LogInfo(string message) => Log(message, MessageType.Info);
}
