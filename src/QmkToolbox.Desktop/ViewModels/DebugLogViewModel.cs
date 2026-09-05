using QmkToolbox.Core.Models;

namespace QmkToolbox.Desktop.ViewModels;

public partial class DebugLogViewModel(
    Func<Func<Task>, Task> uiInvoker, Func<string, Task> setClipboardText)
    : LogViewModelBase(uiInvoker, setClipboardText)
{
    // Callers arrive already marshalled via the composition-root trace sink (App.axaml.cs),
    // so no marshalling happens here. Debug is a line type, so Log ends the line itself and
    // needs no trailing '\n'.
    public void Append(string message) =>
        Log($"{DateTime.Now:HH:mm:ss.fff}  {message}", MessageType.Debug);
}
