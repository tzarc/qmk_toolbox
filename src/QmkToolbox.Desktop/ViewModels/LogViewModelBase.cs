using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QmkToolbox.Core.Models;
using QmkToolbox.Desktop.Models;

namespace QmkToolbox.Desktop.ViewModels;

/// <summary>
/// Base for viewmodels that own a terminal buffer. The UI-thread marshaller and the clipboard
/// writer arrive at construction, so a log-producing callback can never run before they exist.
/// </summary>
/// <param name="maxLogLines">Lines the buffer keeps before trimming the oldest.</param>
public abstract partial class LogViewModelBase(
    Func<Func<Task>, Task> uiInvoker, Func<string, Task> setClipboardText, int maxLogLines = 10_000) : ObservableObject
{
    public TerminalBuffer Buffer { get; } = new();

    protected void Invoke(Action action) =>
        _ = uiInvoker(() => { action(); return Task.CompletedTask; });

    // Writes to the log, routing on the message type's stream discipline (see
    // MessageTypeDescriptor.IsRawStream):
    //  - Raw types (tool stdout/stderr, HID console) go straight to the buffer, which interprets
    //    '\r'/'\n' like a terminal and invents no line breaks: Log("#") three times renders "###".
    //  - Line types (status, errors, command echo) are discrete: they start at column 0, breaking
    //    a partial raw-stream line if one is pending, and end the line.
    public void Log(string text, MessageType type)
    {
        if (MessageTypeDescriptors.For(type).IsRawStream)
            Buffer.Write(text, type);
        else
            Buffer.Write(Buffer.Col > 0 ? "\n" + text + "\n" : text + "\n", type);
        Buffer.TrimToMax(maxLogLines);
    }

    [RelayCommand]
    private void Clear() => Buffer.Clear();

    [RelayCommand]
    private async Task CopyAllAsync() => await setClipboardText(TerminalProjection.ToText(Buffer));
}
