using QmkToolbox.Usb.Hid.Linux;
using Xunit;

namespace QmkToolbox.Usb.Hid.Tests.Linux;

/// <summary>
/// Exercises the sysfs ancestry check against a temp tree shaped like real sysfs: class
/// entries are relative symlinks to hidraw nodes nested under their USB device directory.
/// The load-bearing case is a second identical device, which must not own the node.
/// </summary>
public sealed class LinuxHidOwnershipTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("hid-owner-test-").FullName;

    private string ClassHidraw => Path.Combine(_root, "class", "hidraw");

    public LinuxHidOwnershipTests()
    {
        Directory.CreateDirectory(ClassHidraw);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string AddHidraw(string name, string usbName)
    {
        string usbDir = Path.Combine(_root, "devices", usbName);
        string node = Path.Combine(usbDir, usbName + ":1.1", "0003:FEED:0001.0001", "hidraw", name);
        Directory.CreateDirectory(node);
        Directory.CreateSymbolicLink(Path.Combine(ClassHidraw, name), Path.GetRelativePath(ClassHidraw, node));
        return usbDir;
    }

    [FactOnLinux]
    public void NodeUnderTheDevice_IsOwned()
    {
        string owner = AddHidraw("hidraw4", "3-3");

        Assert.True(LinuxHidOwnership.IsOwnedBy("/dev/hidraw4", owner, ClassHidraw));
    }

    [FactOnLinux]
    public void IdenticalSiblingDevice_DoesNotOwnTheNode()
    {
        _ = AddHidraw("hidraw4", "3-3");
        string sibling = Path.Combine(_root, "devices", "3-4");
        Directory.CreateDirectory(sibling);

        Assert.False(LinuxHidOwnership.IsOwnedBy("/dev/hidraw4", sibling, ClassHidraw));
    }

    [FactOnLinux]
    public void UnknownNode_IsNotOwned() =>
        Assert.False(LinuxHidOwnership.IsOwnedBy("/dev/hidraw9", Path.Combine(_root, "devices", "3-3"), ClassHidraw));
}
