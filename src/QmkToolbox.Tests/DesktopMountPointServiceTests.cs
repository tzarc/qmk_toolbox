using Qmk.Usb.Discovery;
using QmkToolbox.Desktop.Services;
using Xunit;

namespace QmkToolbox.Tests;

/// <summary>
/// Covers the Linux mount-point scan over a fake /proc/mounts table and volume directories:
/// marker filtering, mount-root filtering, and newest-mount-wins. Fixture volumes have no
/// resolvable block device, so ownership is unknown and never filters; Qmk.Usb.Discovery's
/// tests cover the ownership policy.
/// </summary>
public sealed class DesktopMountPointServiceTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("qmk-mounts-test-").FullName;
    private readonly string _procMounts;
    private readonly List<string> _mountLines = [];

    public DesktopMountPointServiceTests()
    {
        _procMounts = Path.Combine(_root, "mounts");
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static UsbDeviceInfo Device() => new(0x2E8A, 0x0003, 0, "", "", "", "");

    private string AddVolume(string name, string blockName, bool withMarker = true)
    {
        string mountPoint = Path.Combine(_root, "media", name);
        Directory.CreateDirectory(mountPoint);
        if (withMarker)
            File.WriteAllText(Path.Combine(mountPoint, "INFO_UF2.TXT"), "UF2 Bootloader v3.0\n");
        _mountLines.Add($"/dev/{blockName} {mountPoint} vfat rw 0 0");
        return mountPoint;
    }

    private string? Find()
    {
        File.WriteAllLines(_procMounts, _mountLines);
        return DesktopMountPointService.FindMountPointLinux(
            Device(), "INFO_UF2.TXT", _procMounts,
            mountRoots: [Path.Combine(_root, "media") + "/"]);
    }

    [FactOnLinux]
    public void MarkerVolume_IsReturned()
    {
        string mount = AddVolume("RPI-RP2", "sdb1");

        Assert.Equal(mount, Find());
    }

    [FactOnLinux]
    public void TwoMarkerVolumes_NewestMountWins()
    {
        AddVolume("OLD", "sdb1");
        string newer = AddVolume("NEW", "sdc1");

        Assert.Equal(newer, Find());
    }

    [FactOnLinux]
    public void VolumeWithoutMarker_IsIgnored()
    {
        AddVolume("PLAIN", "sdb1", withMarker: false);

        Assert.Null(Find());
    }

    [FactOnLinux]
    public void VolumeOutsideMountRoots_IsIgnored()
    {
        // The mount table entry points outside the accepted roots.
        string mountPoint = Path.Combine(_root, "elsewhere", "RPI-RP2");
        Directory.CreateDirectory(mountPoint);
        File.WriteAllText(Path.Combine(mountPoint, "INFO_UF2.TXT"), "UF2 Bootloader v3.0\n");
        _mountLines.Add($"/dev/sdb1 {mountPoint} vfat rw 0 0");

        Assert.Null(Find());
    }
}
