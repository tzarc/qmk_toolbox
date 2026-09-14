using QmkToolbox.Core.Models;

namespace QmkToolbox.Desktop.ViewModels;

/// <summary>
/// The Debug Log's contents. The ViewModel outlives its window, so trace produced before the
/// user opens the window is still there when they do.
/// </summary>
public partial class DebugLogViewModel(
    Func<Func<Task>, Task> uiInvoker, Func<string, Task> setClipboardText)
    : LogViewModelBase(uiInvoker, setClipboardText, MaxTraceLines)
{
    // Trace runs unattended for the whole session and every USB event adds lines, so the
    // Debug Log keeps a shorter history than the main log.
    private const int MaxTraceLines = 2_000;

    // The composition-root sink (App.axaml.cs) marshals callers to the UI thread before they
    // reach here, so this method does not. Debug is a line type, so Log ends the line itself
    // and needs no trailing '\n'.
    public void Append(string message) =>
        Log($"{DateTime.Now:HH:mm:ss.fff}  {message}", MessageType.Debug);
}
