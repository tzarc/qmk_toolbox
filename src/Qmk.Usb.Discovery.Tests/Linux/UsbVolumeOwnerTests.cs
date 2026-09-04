using Xunit;

namespace Qmk.Usb.Discovery.Tests.Linux;

/// <summary>
/// Drives the Linux volume-ownership resolution against a fixture-owned fake /proc/mounts and
/// /sys/class/block tree whose symlinks encode which USB device backs each volume: the
/// coverage for the cross-binding bug where an unrelated storage device's probe claimed
/// another device's marker volume.
/// </summary>
public sealed class UsbVolumeOwnerTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("qmk-volume-owner-test-").FullName;
    private readonly string _procMounts;
    private readonly string _sysBlock;
    private readonly string _deviceSyspath;
    private readonly List<string> _mountLines = [];

    public UsbVolumeOwnerTests()
    {
        _procMounts = Path.Combine(_root, "mounts");
        _sysBlock = Path.Combine(_root, "sys-class-block");
        Directory.CreateDirectory(_sysBlock);
        // The canonical syspath of "our" USB device, as the tracker reports it.
        _deviceSyspath = Path.Combine(_root, "sys-devices", "usb3", "3-1");
        Directory.CreateDirectory(_deviceSyspath);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private UsbDeviceInfo Device() => new(0x2E8A, 0x0003, 0, "", "", "", _deviceSyspath);

    /// <summary>Registers a mounted volume backed by a block device beneath <paramref name="ownerSyspath"/> (or unlinked when null).</summary>
    private string AddVolume(string name, string blockName, string? ownerSyspath, string source = "/dev/")
    {
        string mountPoint = Path.Combine(_root, "media", name);
        Directory.CreateDirectory(mountPoint);
        if (ownerSyspath != null)
        {
            string blockSyspath = Path.Combine(ownerSyspath, $"{blockName}-intf", "block", blockName);
            Directory.CreateDirectory(blockSyspath);
            Directory.CreateSymbolicLink(Path.Combine(_sysBlock, blockName), blockSyspath);
        }
        _mountLines.Add($"{source}{blockName} {mountPoint} vfat rw 0 0");
        return mountPoint;
    }

    private bool? BelongsTo(string mountPoint, UsbDeviceInfo device)
    {
        File.WriteAllLines(_procMounts, _mountLines);
        return UsbVolumeOwner.BelongsToLinux(mountPoint, device, _procMounts, _sysBlock);
    }

    [FactOnLinux]
    public void OwnVolume_Belongs()
    {
        string mount = AddVolume("RPI-RP2", "sdb1", _deviceSyspath);

        Assert.True(BelongsTo(mount, Device()));
    }

    [FactOnLinux]
    public void VolumeBackedByDifferentDevice_DoesNotBelong()
    {
        string otherDevice = Path.Combine(_root, "sys-devices", "usb3", "3-2");
        Directory.CreateDirectory(otherDevice);
        string mount = AddVolume("RPI-RP2", "sdb1", otherDevice);

        Assert.False(BelongsTo(mount, Device()));
    }

    [FactOnLinux]
    public void UnresolvableBlockDevice_Unknown()
    {
        // No /sys/class/block entry for the source.
        string mount = AddVolume("RPI-RP2", "sdz9", ownerSyspath: null);

        Assert.Null(BelongsTo(mount, Device()));
    }

    [FactOnLinux]
    public void MountPointNotInTable_Unknown()
    {
        AddVolume("RPI-RP2", "sdb1", _deviceSyspath);

        Assert.Null(BelongsTo(Path.Combine(_root, "media", "OTHER"), Device()));
    }

    [FactOnLinux]
    public void NonDeviceSource_Unknown()
    {
        // e.g. a tmpfs mount; no block device to resolve.
        string mount = AddVolume("RAMDISK", "tmpfs", ownerSyspath: null, source: "");

        Assert.Null(BelongsTo(mount, Device()));
    }

    [FactOnLinux]
    public void DeviceWithoutSyspath_Unknown()
    {
        string mount = AddVolume("RPI-RP2", "sdb1", _deviceSyspath);
        var pathless = new UsbDeviceInfo(0x2E8A, 0x0003, 0, "", "", "", "");

        Assert.Null(BelongsTo(mount, pathless));
    }
}
