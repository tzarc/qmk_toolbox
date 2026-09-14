using Avalonia.Input;
using QmkToolbox.Desktop.Models;
using QmkToolbox.Desktop.ViewModels;
using Xunit;

namespace QmkToolbox.Tests;

public class KeyTesterViewModelTests
{
    private readonly KeyTesterViewModel _vm = new();

    // ── layout table sanity ───────────────────────────────────────────────────

    [Fact]
    public void EveryPhysicalKey_AppearsOnce() =>
        Assert.Equal(_vm.Keys.Length, _vm.Keys.Select(k => k.Key).Distinct().Count());

    [Fact]
    public void NoTwoKeys_Overlap()
    {
        KeyViewModel[] keys = _vm.Keys;
        for (int i = 0; i < keys.Length; i++)
        {
            for (int j = i + 1; j < keys.Length; j++)
            {
                KeyViewModel a = keys[i];
                KeyViewModel b = keys[j];
                bool overlap = a.X < b.X + b.Width && b.X < a.X + a.Width
                    && a.Y < b.Y + b.Height && b.Y < a.Y + a.Height;
                Assert.False(overlap, $"{a.Key} at ({a.X},{a.Y}) overlaps {b.Key} at ({b.X},{b.Y})");
            }
        }
    }

    // ── state transitions ─────────────────────────────────────────────────────

    [Fact]
    public void KeyDown_MarksPressed_AndReportsTheKey()
    {
        _vm.OnKeyDown(PhysicalKey.A);

        Assert.Equal(KeyState.Pressed, _vm.Keys.Single(k => k.Key == PhysicalKey.A).State);
        Assert.Equal("A", _vm.LastKeycode);
        Assert.Equal($"0x{(int)PhysicalKey.A:X4}", _vm.LastScanCode);
    }

    [Fact]
    public void KeyUp_MarksTested()
    {
        _vm.OnKeyDown(PhysicalKey.Space);
        _vm.OnKeyUp(PhysicalKey.Space);

        Assert.Equal(KeyState.Tested, _vm.Keys.Single(k => k.Key == PhysicalKey.Space).State);
    }

    [Fact]
    public void Reset_ClearsAllStatesAndTheReadout()
    {
        _vm.OnKeyDown(PhysicalKey.Q);
        _vm.OnKeyUp(PhysicalKey.Q);
        _vm.OnKeyDown(PhysicalKey.W);

        _vm.ResetCommand.Execute(null);

        Assert.All(_vm.Keys, k => Assert.Equal(KeyState.Default, k.State));
        Assert.Equal("", _vm.LastKeycode);
        Assert.Equal("", _vm.LastScanCode);
    }

    // Keys outside the layout table (e.g. media keys) still update the readout.
    [Fact]
    public void UnmappedKey_IsReportedWithoutError()
    {
        _vm.OnKeyDown(PhysicalKey.MediaPlayPause);
        _vm.OnKeyUp(PhysicalKey.MediaPlayPause);

        Assert.Equal("MediaPlayPause", _vm.LastKeycode);
        Assert.All(_vm.Keys, k => Assert.Equal(KeyState.Default, k.State));
    }
}
