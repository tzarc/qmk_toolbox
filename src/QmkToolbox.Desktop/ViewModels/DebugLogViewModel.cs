using QmkToolbox.Core.Models;

namespace QmkToolbox.Desktop.ViewModels;

public partial class DebugLogViewModel : LogViewModelBase
{
    // Debug is a line type, so Log ends the line itself and needs no trailing '\n'.
    public void Append(string message) =>
        Log($"{DateTime.Now:HH:mm:ss.fff}  {message}", MessageType.Debug);
}
