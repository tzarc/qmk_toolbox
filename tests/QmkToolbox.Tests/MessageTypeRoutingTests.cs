using QmkToolbox.Core.Models;
using QmkToolbox.Desktop.Models;
using QmkToolbox.Desktop.ViewModels;
using Xunit;

namespace QmkToolbox.Tests;

public class MessageTypeRoutingTests
{
    [Theory]
    // Raw byte streams
    [InlineData(MessageType.CommandOutput, true)]
    [InlineData(MessageType.CommandError, true)]
    [InlineData(MessageType.HidOutput, true)]
    // Discrete, line-oriented messages
    [InlineData(MessageType.Bootloader, false)]
    [InlineData(MessageType.Command, false)]
    [InlineData(MessageType.Debug, false)]
    [InlineData(MessageType.Error, false)]
    [InlineData(MessageType.Hid, false)]
    [InlineData(MessageType.Info, false)]
    [InlineData(MessageType.Usb, false)]
    [InlineData(MessageType.UdevOutput, false)]
    public void IsRawStream_ClassifiesEveryType(MessageType type, bool expected)
        => Assert.Equal(expected, type.IsRawStream());

    [Fact]
    public void IsRawStream_HandlesEveryMessageType()
    {
        // A new enum value left unhandled must fail here, not at render time.
        foreach (MessageType type in Enum.GetValues<MessageType>())
            _ = type.IsRawStream();
    }

    [Fact]
    public void Log_RawType_AppendsWithoutInventingABreak()
    {
        var vm = new TestLog();

        vm.Log("abc", MessageType.CommandOutput);

        Assert.Equal(3, vm.Buffer.Col);   // cursor advanced within the current line
        Assert.Empty(vm.Buffer.Lines);    // nothing committed
        Assert.Equal("abc", TerminalText.Flatten(vm.Buffer));
    }

    [Fact]
    public void Log_LineType_EndsTheLine()
    {
        var vm = new TestLog();

        vm.Log("hi", MessageType.Info);

        Assert.Equal(0, vm.Buffer.Col);    // line ended, cursor home
        Assert.Single(vm.Buffer.Lines);    // "hi" committed
        Assert.Equal("hi", TerminalText.Flatten(vm.Buffer));
    }

    [Fact]
    public void Log_LineType_BreaksAPendingPartialRawLine()
    {
        var vm = new TestLog();

        vm.Log("ab", MessageType.CommandOutput);  // raw partial line (Col > 0)
        vm.Log("X", MessageType.Info);            // line message breaks it first

        Assert.Equal(0, vm.Buffer.Col);
        Assert.Equal(2, vm.Buffer.Lines.Count);   // "ab" then "X"
        Assert.Equal("ab\nX", TerminalText.Flatten(vm.Buffer));
    }

    private sealed class TestLog() : LogViewModelBase(f => f(), _ => Task.CompletedTask);
}
