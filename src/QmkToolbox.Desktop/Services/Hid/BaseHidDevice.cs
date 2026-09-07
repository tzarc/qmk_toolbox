using QmkToolbox.Usb.Hid;

namespace QmkToolbox.Desktop.Services.Hid;

public abstract class BaseHidDevice(HidInterfaceInfo iface) : IHidDevice
{
    public string ManufacturerString { get; } = iface.Manufacturer;
    public string ProductString { get; } = iface.Product;
    public ushort VendorId { get; } = iface.VendorId;
    public ushort ProductId { get; } = iface.ProductId;
    public ushort RevisionBcd { get; } = iface.RevisionBcd;
    public ushort UsagePage { get; } = iface.UsagePage;
    public ushort Usage { get; } = iface.Usage;
    public string DevicePath { get; } = iface.DevicePath;

    /// <inheritdoc />
    public abstract bool IsConsoleDevice { get; }

    /// <summary>Raised with the decoded text of each console report; only console devices raise it.</summary>
    public event Action<BaseHidDevice, string>? ConsoleReportReceived;

    protected void RaiseConsoleReport(string data) => ConsoleReportReceived?.Invoke(this, data);

    public override string ToString() =>
        $"{ManufacturerString} {ProductString} ({VendorId:X4}:{ProductId:X4}:{RevisionBcd:X4})";
}
