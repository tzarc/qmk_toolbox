using System.Text.RegularExpressions;
using QmkToolbox.Desktop.ViewModels;
using Xunit;

namespace QmkToolbox.Tests;

public class DebugLogViewModelTests
{
    private static DebugLogViewModel NewViewModel() =>
        new(f => f(), _ => Task.CompletedTask);

    [Fact]
    public void Append_StampsTheLineAndEndsIt()
    {
        DebugLogViewModel vm = NewViewModel();

        vm.Append("[USB+] VID:03EB PID:2FF4");
        vm.Append("[ORCH+] not a bootloader");

        Assert.Equal(2, vm.Buffer.Lines.Count);
        Assert.Matches(
            new Regex(@"^\d{2}:\d{2}:\d{2}\.\d{3}  \[USB\+\] VID:03EB PID:2FF4$"),
            TerminalText.Flatten(vm.Buffer).Split('\n')[0]);
    }

    [Fact]
    public void Append_PastTheCap_DropsTheOldestLines()
    {
        DebugLogViewModel vm = NewViewModel();

        // One past the 2,000-line cap, so the first line written must be gone.
        for (int i = 0; i <= 2_000; i++)
            vm.Append($"line {i}");

        string text = TerminalText.Flatten(vm.Buffer);
        Assert.DoesNotContain("line 0\n", text, StringComparison.Ordinal);
        Assert.Contains("line 2000", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Clear_EmptiesTheBuffer()
    {
        DebugLogViewModel vm = NewViewModel();
        vm.Append("[USB+] VID:03EB PID:2FF4");

        vm.ClearCommand.Execute(null);

        Assert.Empty(TerminalText.Flatten(vm.Buffer));
    }
}
