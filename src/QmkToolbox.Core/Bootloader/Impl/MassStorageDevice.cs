using QmkToolbox.Core.Models;
using QmkToolbox.Usb.Discovery;

namespace QmkToolbox.Core.Bootloader.Impl;

/// <summary>Mass-storage bootloader device (copies the firmware file to a mounted volume).</summary>
internal sealed class MassStorageDevice : BootloaderDevice
{
    private readonly MassStorageBootloader _family;

    public string? MountPoint { get; private set; }

    public MassStorageDevice(MassStorageBootloader family, UsbDeviceInfo device, BootloaderServices services, string? boardId = null, string? mountPoint = null)
        : base(device, services)
    {
        _family = family;
        Type = family.Type;
        Name = boardId == null ? family.Name : $"{family.Name} ({boardId})";
        PreferredDriver = "USBSTOR";
        MountPoint = mountPoint;
    }

    public override async Task FlashAsync(string mcu, string file)
    {
        ValidateFileExtension(file, _family.Extensions);

        MountPoint = await FindMountPointAsync(_family.MarkerFile).ConfigureAwait(false);
        if (MountPoint == null)
        {
            Output("Mount point not found! The volume must be mounted before flashing.", MessageType.Error);
            return;
        }
        string destFile = Path.Combine(MountPoint, _family.DestFileName);

        // File.Delete/Copy block, and USB mass storage is slow enough to hang the UI thread.
        // Output is safe from any thread; marshalling to the UI is the composition root's job.
        await Task.Run(() =>
        {
            try
            {
                if (_family.DeleteBeforeCopy)
                {
                    Output($"Deleting {destFile}...", MessageType.Command);
                    File.Delete(destFile);
                }
                Output($"Copying {file} to {destFile}...", MessageType.Command);
                File.Copy(file, destFile, overwrite: true);
                Output(_family.CompletionMessage, MessageType.Bootloader);
            }
            catch (IOException e)
            {
                Output($"IO ERROR: {e.Message}", MessageType.Error);
            }
        }).ConfigureAwait(false);
    }

    public override string ToString() =>
        MountPoint == null ? base.ToString() : $"{base.ToString()} [{MountPoint}]";
}
