using QmkToolbox.Core.Models;
using QmkToolbox.Desktop.Services;
using Xunit;

namespace QmkToolbox.Tests;

/// <summary>
/// Drives the Linux mount-point scan against a fixture-owned fake /proc/mounts, volume
/// directories, and a fake /sys/class/block tree whose symlinks encode which USB device backs
/// each volume: the coverage for the cross-binding bug where an unrelated storage device's
/// probe claimed another device's marker volume.
/// </summary>
public sealed class DesktopMountPointServiceTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("qmk-mounts-test-").FullName;
    private readonly string _procMounts;
    private readonly string _sysBlock;
    private readonly string _deviceSyspath;
    private readonly List<string> _mountLines = [];

    public DesktopMountPointServiceTests()
    {
        _procMounts = Path.Combine(_root, "mounts");
        _sysBlock = Path.Combine(_root, "sys-class-block");
        Directory.CreateDirectory(_sysBlock);
        // The canonical syspath of "our" USB device, as the tracker reports it.
        _deviceSyspath = Path.Combine(_root, "sys-devices", "usb3", "3-1");
        Directory.CreateDirectory(_deviceSyspath);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private IUsbDevice Device() => new UsbDeviceInfo(0x2E8A, 0x0003, 0, "", "", "", _deviceSyspath);

    /// <summary>Creates a marker-carrying volume backed by a block device beneath <paramref name="ownerSyspath"/> (or unlinked when null).</summary>
    private string AddVolume(string name, string blockName, string? ownerSyspath, bool withMarker = true)
    {
        string mountPoint = Path.Combine(_root, "media", name);
        Directory.CreateDirectory(mountPoint);
        if (withMarker)
            File.WriteAllText(Path.Combine(mountPoint, "INFO_UF2.TXT"), "UF2 Bootloader v3.0\n");
        if (ownerSyspath != null)
        {
            string blockSyspath = Path.Combine(ownerSyspath, $"{blockName}-intf", "block", blockName);
            Directory.CreateDirectory(blockSyspath);
            Directory.CreateSymbolicLink(Path.Combine(_sysBlock, blockName), blockSyspath);
        }
        // /proc/mounts format: source mountpoint fstype options dump pass.
        _mountLines.Add($"/dev/{blockName} {mountPoint} vfat rw 0 0");
        return mountPoint;
    }

    private string? Find(IUsbDevice device)
    {
        File.WriteAllLines(_procMounts, _mountLines);
        return DesktopMountPointService.FindMountPointLinux(
            device, "INFO_UF2.TXT", _procMounts, _sysBlock,
            mountRoots: [Path.Combine(_root, "media") + "/"]);
    }

    [FactOnLinux]
    public void OwnVolume_IsReturned()
    {
        string mount = AddVolume("RPI-RP2", "sdb1", _deviceSyspath);

        Assert.Equal(mount, Find(Device()));
    }

    [FactOnLinux]
    public void VolumeBackedByDifferentDevice_IsSkipped()
    {
        // The marker volume belongs to another USB device, so the probing device must not claim it.
        string otherDevice = Path.Combine(_root, "sys-devices", "usb3", "3-2");
        Directory.CreateDirectory(otherDevice);
        AddVolume("RPI-RP2", "sdb1", otherDevice);

        Assert.Null(Find(Device()));
    }

    [FactOnLinux]
    public void VolumeWithUnresolvableOwnership_IsAccepted()
    {
        // No /sys/class/block entry for the source: ownership unknown, pre-correlation behaviour.
        string mount = AddVolume("RPI-RP2", "sdz9", ownerSyspath: null);

        Assert.Equal(mount, Find(Device()));
    }

    [FactOnLinux]
    public void DeviceWithoutSyspath_AcceptsAnyMarkerVolume()
    {
        string otherDevice = Path.Combine(_root, "sys-devices", "usb3", "3-2");
        Directory.CreateDirectory(otherDevice);
        string mount = AddVolume("RPI-RP2", "sdb1", otherDevice);
        IUsbDevice pathless = new UsbDeviceInfo(0x2E8A, 0x0003, 0, "", "", "", "");

        Assert.Equal(mount, Find(pathless));
    }

    [FactOnLinux]
    public void TwoOwnVolumes_NewestMountWins()
    {
        AddVolume("OLD", "sdb1", _deviceSyspath);
        string newer = AddVolume("NEW", "sdc1", _deviceSyspath);

        Assert.Equal(newer, Find(Device()));
    }

    [FactOnLinux]
    public void VolumeWithoutMarker_IsIgnored()
    {
        AddVolume("PLAIN", "sdb1", _deviceSyspath, withMarker: false);

        Assert.Null(Find(Device()));
    }
}
