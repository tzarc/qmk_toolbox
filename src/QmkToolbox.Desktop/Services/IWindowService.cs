namespace QmkToolbox.Desktop.Services;

/// <summary>
/// Window-facing operations the main ViewModel triggers: file picking and the auxiliary
/// windows. The desktop adapter is <see cref="DesktopWindowService"/>; tests substitute a fake
/// so the ViewModel is constructible without a running Avalonia app.
/// </summary>
public interface IWindowService
{
    Task<string?> PickFirmwareFileAsync();
    void ShowKeyTester();
    void ShowHidConsole();
    void ShowAbout();
    void ShowDebugLog();
}
