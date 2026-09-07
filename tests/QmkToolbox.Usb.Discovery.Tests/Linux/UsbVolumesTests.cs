using Xunit;

namespace QmkToolbox.Usb.Discovery.Tests.Linux;

/// <summary>
/// Drives the Linux volume enumeration against a fixture-owned fake /proc/mounts and
/// /sys/class/block tree whose symlinks encode which USB device backs each volume. Only
/// provably owned volumes may appear; unknown ownership excludes a volume here (unlike
/// <see cref="UsbVolumeOwner.OwnsVolume"/>, which reports it as null for the caller to judge).
/// </summary>
public sealed class UsbVolumesTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("qmk-volumes-test-").FullName;
    private readonly string _procMounts;
    private readonly string _sysBlock;
    private readonly string _deviceSyspath;
    private readonly List<string> _mountLines = [];

    public UsbVolumesTests()
    {
        _procMounts = Path.Combine(_root, "mounts");
        _sysBlock = Path.Combine(_root, "sys-class-block");
        Directory.CreateDirectory(_sysBlock);
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

    private List<string> Enumerate()
    {
        File.WriteAllLines(_procMounts, _mountLines);
        return [.. UsbVolumes.EnumerateVolumesLinux(Device(), _procMounts, _sysBlock)];
    }

    [FactOnLinux]
    public void OwnVolumes_AreEnumeratedInMountOrder()
    {
        string first = AddVolume("RPI-RP2", "sdb1", _deviceSyspath);
        string second = AddVolume("PRIMEPLUS", "sdb2", _deviceSyspath);

        Assert.Equal([first, second], Enumerate());
    }

    [FactOnLinux]
    public void OtherDevicesVolume_IsExcluded()
    {
        string otherSyspath = Path.Combine(_root, "sys-devices", "usb3", "3-2");
        Directory.CreateDirectory(otherSyspath);
        AddVolume("THEIRS", "sdc1", otherSyspath);
        string ours = AddVolume("OURS", "sdb1", _deviceSyspath);

        Assert.Equal([ours], Enumerate());
    }

    [FactOnLinux]
    public void UnknownOwnership_IsExcluded()
    {
        AddVolume("MYSTERY", "sdd1", ownerSyspath: null);

        Assert.Empty(Enumerate());
    }

    [FactOnLinux]
    public void NonDeviceSource_IsExcluded()
    {
        AddVolume("TMPFS", "shm", _deviceSyspath, source: "");

        Assert.Empty(Enumerate());
    }

    [Fact]
    public void MissingTable_YieldsNothing() =>
        Assert.Empty(UsbVolumes.EnumerateVolumesLinux(
            Device(), Path.Combine(_root, "does-not-exist"), _sysBlock));
}
