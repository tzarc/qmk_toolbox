using NSubstitute;
using QmkToolbox.Core.Bootloader;
using QmkToolbox.Core.Models;
using QmkToolbox.Core.Services;
using QmkToolbox.Usb.Discovery;
using Xunit;

namespace QmkToolbox.Tests;

public class FlashOrchestratorTests
{
    private static FlashOrchestrator NewOrchestrator(
        IUsbDeviceResolver? resolver = null, MessageSink? output = null, IProcessRunner? runner = null) => new(
        new BootloaderServices(
            Substitute.For<IFlashToolProvider>(), resolver ?? new FakeUsbResolver(), output ?? ((_, _) => { }))
        {
            VolumeProbeDelayMs = 1,
            ProcessRunner = runner ?? new CapturingProcessRunner(),
        });

    /// <summary>Hands back a fresh stuck process per launch, as a tool waiting on a dead port would.</summary>
    private sealed class HangingProcessRunner : IProcessRunner
    {
        public IRunningProcess Start(string fileName, string workingDir, IReadOnlyList<string> args) =>
            new FakeRunningProcess(hang: true);
    }

    private static FakeUsbResolver ResolverWithVolume(string mountPoint)
    {
        var resolver = new FakeUsbResolver();
        resolver.Volumes.Add(mountPoint);
        return resolver;
    }

    // VID/PID are arbitrary: marker-probed devices are outside the VID/PID map by construction.
    private static UsbDeviceInfo MassStorage(ushort pid = 0x00FF, string path = "path0") =>
        new(0x239A, pid, 0, "Test", "Board", path, isMassStorage: true);

    private static UsbDeviceInfo Unknown() =>
        new(0x1234, 0x5678, 0, "", "", "path1");

    // LUFA HID: one tool invocation per flash, so a stuck tool is the whole operation.
    private static UsbDeviceInfo LufaHid() =>
        new(0x03EB, 0x2067, 0, "", "", "path2");

    [Fact]
    public async Task RemovingTheDeviceUnderFlash_StopsTheToolAndFreesTheAppAsync()
    {
        var messages = new List<string>();
        var toolStarted = new TaskCompletionSource();
        FlashOrchestrator orch = NewOrchestrator(
            runner: new HangingProcessRunner(),
            output: (msg, type) =>
            {
                lock (messages)
                    messages.Add(msg);
                if (type == MessageType.Command)
                    toolStarted.TrySetResult();
            });

        UsbDeviceInfo device = LufaHid();
        Assert.True(await orch.OnDeviceConnectedAsync(device, false));

        Task<bool> flash = orch.FlashAllAsync("atmega32u4", "firmware.hex");
        await toolStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(orch.IsBusy);

        orch.OnDeviceDisconnected(device, false);

        // Without cancellation this waits out the tool's own five-minute timeout.
        Assert.True(await flash.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.False(orch.IsBusy);
        lock (messages)
            Assert.Contains("Flash tool stopped: the device was removed.", messages);
    }

    [Fact]
    public async Task RemovingAnUnrelatedDevice_LeavesTheFlashRunningAsync()
    {
        var toolStarted = new TaskCompletionSource();
        FlashOrchestrator orch = NewOrchestrator(
            runner: new HangingProcessRunner(),
            output: (_, type) =>
            {
                if (type == MessageType.Command)
                    toolStarted.TrySetResult();
            });

        UsbDeviceInfo target = LufaHid();
        Assert.True(await orch.OnDeviceConnectedAsync(target, false));

        Task<bool> flash = orch.FlashAllAsync("atmega32u4", "firmware.hex");
        await toolStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        orch.OnDeviceDisconnected(Unknown(), false);

        Assert.False(flash.IsCompleted);
        Assert.True(orch.IsBusy);

        // Let the test finish rather than leaving the tool stuck for its full timeout.
        orch.OnDeviceDisconnected(target, false);
        Assert.True(await flash.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    // ── UF2 volume probe ──────────────────────────────────────────────────────

    [Fact]
    public async Task OnDeviceConnectedAsync_MassStorageWithUf2Volume_RegistersBootloaderAsync()
    {
        string mountDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(mountDir);
        await File.WriteAllTextAsync(Path.Combine(mountDir, "INFO_UF2.TXT"),
            "UF2 Bootloader v3.0\nModel: Test Board\nBoard-ID: TEST-V1\n");

        try
        {
            var connected = new TaskCompletionSource<string>();
            FlashOrchestrator orch = NewOrchestrator(ResolverWithVolume(mountDir), (msg, type) =>
            {
                if (type == MessageType.Bootloader && msg.Contains("device connected"))
                    connected.TrySetResult(msg);
            });

            Assert.True(await orch.OnDeviceConnectedAsync(MassStorage(), false));
            Assert.True(orch.HasBootloaders);

            // A WhenReadyAsync continuation on the thread pool emits the connected message;
            // wait for it rather than asserting immediately.
            string msg = await connected.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.StartsWith("UF2 (TEST-V1) device connected", msg);
        }
        finally
        {
            Directory.Delete(mountDir, true);
        }
    }

    [Fact]
    public async Task OnDeviceConnectedAsync_VolumeMountedLate_RegistersOnMountAsync()
    {
        string mountDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(mountDir);
        await File.WriteAllTextAsync(Path.Combine(mountDir, "INFO_UF2.TXT"), "UF2 Bootloader v3.0\n");

        try
        {
            // Desktops that don't automount (e.g. KDE) surface the volume only when the
            // user clicks mount; several polls after arrival stand in for that.
            IUsbDeviceResolver resolver = Substitute.For<IUsbDeviceResolver>();
            resolver.EnumerateVolumes(Arg.Any<UsbDeviceInfo>()).Returns([], [], [], [], [mountDir]);
            FlashOrchestrator orch = NewOrchestrator(resolver);

            Assert.True(await orch.OnDeviceConnectedAsync(MassStorage(), false).WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.True(orch.HasBootloaders);
        }
        finally
        {
            Directory.Delete(mountDir, true);
        }
    }

    [Fact]
    public async Task OnDeviceConnectedAsync_VolumeNeverMounts_ProbesUntilRemovalAsync()
    {
        FlashOrchestrator orch = NewOrchestrator();
        UsbDeviceInfo device = MassStorage();

        Task<bool> connect = orch.OnDeviceConnectedAsync(device, false);
        await Task.Delay(50);
        Assert.False(connect.IsCompleted);

        orch.OnDeviceDisconnected(device, false);

        Assert.False(await connect.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.False(orch.HasBootloaders);
    }

    [Fact]
    public async Task OnDeviceConnectedAsync_VolumeAlreadyClaimed_SecondDeviceKeepsProbingAsync()
    {
        string mountDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(mountDir);
        await File.WriteAllTextAsync(Path.Combine(mountDir, "INFO_UF2.TXT"), "UF2 Bootloader v3.0\n");

        try
        {
            FlashOrchestrator orch = NewOrchestrator(ResolverWithVolume(mountDir));
            UsbDeviceInfo second = MassStorage(pid: 0x0002, path: "path2");

            Assert.True(await orch.OnDeviceConnectedAsync(MassStorage(pid: 0x0001, path: "path1"), false));

            // The volume backs the first device now, so the second must not claim it too.
            Task<bool> connect = orch.OnDeviceConnectedAsync(second, false);
            await Task.Delay(50);
            Assert.False(connect.IsCompleted);

            orch.OnDeviceDisconnected(second, false);

            Assert.False(await connect.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.True(orch.HasBootloaders);
        }
        finally
        {
            Directory.Delete(mountDir, true);
        }
    }

    [Fact]
    public async Task OnDeviceConnectedAsync_UnknownNonMassStorage_NotProbedAsync()
    {
        IUsbDeviceResolver resolver = Substitute.For<IUsbDeviceResolver>();
        FlashOrchestrator orch = NewOrchestrator(resolver);

        Assert.False(await orch.OnDeviceConnectedAsync(Unknown(), false));

        Assert.False(orch.HasBootloaders);
        resolver.DidNotReceive().EnumerateVolumes(Arg.Any<UsbDeviceInfo>());
    }

    [Fact]
    public async Task OnDeviceDisconnected_RemovesUf2BootloaderAsync()
    {
        string mountDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(mountDir);
        await File.WriteAllTextAsync(Path.Combine(mountDir, "INFO_UF2.TXT"), "UF2 Bootloader v3.0\n");

        try
        {
            FlashOrchestrator orch = NewOrchestrator(ResolverWithVolume(mountDir));
            UsbDeviceInfo device = MassStorage();
            await orch.OnDeviceConnectedAsync(device, false);
            Assert.True(orch.HasBootloaders);

            orch.OnDeviceDisconnected(device, false);

            Assert.False(orch.HasBootloaders);
        }
        finally
        {
            Directory.Delete(mountDir, true);
        }
    }

    [Fact]
    public async Task RunExclusiveAsync_WhileBusy_SecondCallRefusedAsync()
    {
        FlashOrchestrator orch = NewOrchestrator();
        var gate = new TaskCompletionSource();

        Task<bool> first = orch.RunExclusiveAsync(() => gate.Task);
        Assert.True(orch.IsBusy);
        Assert.False(first.IsCompleted);

        bool second = await orch.RunExclusiveAsync(() => Task.CompletedTask);
        Assert.False(second);
        Assert.True(orch.IsBusy);

        gate.SetResult();
        Assert.True(await first);
        Assert.False(orch.IsBusy);
    }

    [Fact]
    public async Task RunExclusiveAsync_OperationThrows_ResetsBusyAndPropagatesAsync()
    {
        FlashOrchestrator orch = NewOrchestrator();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => orch.RunExclusiveAsync(() => throw new InvalidOperationException()));

        Assert.False(orch.IsBusy);
    }
}
