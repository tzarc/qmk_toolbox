namespace QmkToolbox.Core.Models;

public interface IUsbDevice
{
    ushort VendorId { get; }
    ushort ProductId { get; }
    ushort RevisionBcd { get; }
    string ManufacturerString { get; }
    string ProductString { get; }
    string Driver { get; }
    string DevicePath { get; }
}
