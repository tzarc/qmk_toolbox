namespace QmkToolbox.Desktop.Services;

/// <summary>
/// Window-facing operations the main ViewModel triggers: file picking, auxiliary windows, and
/// the debug-log trace. The desktop adapter is <see cref="DesktopWindowService"/>; tests
/// substitute a fake so the ViewModel is constructible without a running Avalonia app.
/// </summary>
public interface IWindowService
{
    Task<string?> PickFirmwareFileAsync();
    void ShowKeyTester();
    void ShowHidConsole();
    void ShowAbout();
    void ShowDebugLog();

    /// <summary>Appends a diagnostic trace line to the Debug Log window. No-op when the window is not open.</summary>
    void TraceDebug(string message);
}
