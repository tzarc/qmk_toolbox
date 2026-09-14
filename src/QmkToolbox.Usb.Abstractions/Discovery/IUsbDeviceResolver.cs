namespace QmkToolbox.Usb.Discovery;

/// <summary>
/// Resolves what the operating system attached for a USB device: the serial ports it exposes
/// and the volumes it backs. Ports and volumes appear some time after the connect event, so
/// retry on an empty result.
/// </summary>
public interface IUsbDeviceResolver
{
    /// <summary>
    /// Enumerates the serial ports backed by <paramref name="device"/>: Linux device nodes
    /// (<c>/dev/ttyACM0</c>), macOS callout devices (<c>/dev/cu.usbmodem1101</c>), or Windows
    /// COM port names (<c>COM3</c>). A device with several serial interfaces yields them all,
    /// primary interface first; a device with none yields an empty sequence.
    /// </summary>
    IEnumerable<string> EnumerateSerialPorts(UsbDeviceInfo device);

    /// <summary>
    /// Enumerates the mount points of the volumes provably backed by
    /// <paramref name="device"/>: Windows drive roots (<c>E:\</c>), Linux mount points
    /// (<c>/media/user/VOLUME</c>), or macOS volume paths (<c>/Volumes/VOLUME</c>), in platform
    /// enumeration order. Omit any volume whose ownership cannot be proven.
    /// </summary>
    IEnumerable<string> EnumerateVolumes(UsbDeviceInfo device);
}
