using QmkToolbox.Core.Models;

namespace QmkToolbox.Core.Services;

public interface IMountPointService
{
    string? FindMountPoint(IUsbDevice device);
}
