namespace QmkToolbox.Core.Models;

public interface IHidDevice
{
    ushort VendorId { get; }
    ushort ProductId { get; }
    ushort RevisionBcd { get; }
    ushort UsagePage { get; }
    ushort Usage { get; }
    string ManufacturerString { get; }
    string ProductString { get; }
    string DevicePath { get; }
    bool IsConsoleDevice { get; }
}
