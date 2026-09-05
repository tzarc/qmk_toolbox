using QmkToolbox.Core.Models;

namespace QmkToolbox.Desktop.ViewModels;

public partial class DebugLogViewModel(
    Func<Func<Task>, Task> uiInvoker, Func<string, Task> setClipboardText)
    : LogViewModelBase(uiInvoker, setClipboardText)
{
    // The composition-root trace sink (App.axaml.cs) marshals callers to the UI thread before
    // they reach here, so this method does not. Debug is a line type, so Log ends the line
    // itself and needs no trailing '\n'.
    public void Append(string message) =>
        Log($"{DateTime.Now:HH:mm:ss.fff}  {message}", MessageType.Debug);
}
