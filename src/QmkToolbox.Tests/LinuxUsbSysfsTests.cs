using QmkToolbox.Desktop.Services;
using Xunit;

namespace QmkToolbox.Tests;

/// <summary>
/// Drives <see cref="LinuxUsbSysfs.HasMassStorageInterface"/> against a fixture-owned fake
/// sysfs device node with realistic attribute formatting (bNumInterfaces is "%2d", interface
/// directories are "bus-port:config.iface" with two-hex-digit bInterfaceClass).
/// </summary>
public sealed class LinuxUsbSysfsTests : IDisposable
{
    private readonly string _syspath = Directory.CreateTempSubdirectory("qmk-sysfs-msc-test-").FullName;

    public void Dispose() => Directory.Delete(_syspath, recursive: true);

    private void SetInterfaceCount(int count) =>
        File.WriteAllText(Path.Combine(_syspath, "bNumInterfaces"), $" {count}\n");

    private void AddInterface(string name, string interfaceClass)
    {
        string dir = Path.Combine(_syspath, name);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "bInterfaceClass"), interfaceClass + "\n");
    }

    [Fact]
    public void SingleMassStorageInterface_Detected()
    {
        SetInterfaceCount(1);
        AddInterface("1-2:1.0", "08");

        Assert.True(LinuxUsbSysfs.HasMassStorageInterface(_syspath));
    }

    [Fact]
    public void CompositeDevice_WithStorageFunction_Detected()
    {
        SetInterfaceCount(2);
        AddInterface("1-2:1.0", "03"); // HID
        AddInterface("1-2:1.1", "08"); // mass storage

        Assert.True(LinuxUsbSysfs.HasMassStorageInterface(_syspath));
    }

    [Fact]
    public void HidOnlyDevice_NotMassStorage()
    {
        SetInterfaceCount(1);
        AddInterface("1-2:1.0", "03");

        Assert.False(LinuxUsbSysfs.HasMassStorageInterface(_syspath));
    }

    [Fact]
    public void InterfacesNeverRegister_SettlesToFalse()
    {
        // The kernel emits the device uevent before interfaces exist; if they never appear,
        // the settle loop must give up rather than spin. (~400ms of real settle delay.)
        SetInterfaceCount(1);

        Assert.False(LinuxUsbSysfs.HasMassStorageInterface(_syspath));
    }

    [Fact]
    public void MissingSyspath_False() =>
        Assert.False(LinuxUsbSysfs.HasMassStorageInterface(Path.Combine(_syspath, "missing")));
}
