namespace QmkToolbox.Usb.Hid.Linux;

/// <summary>
/// Resolves whether a hidraw node belongs to a USB device by checking sysfs ancestry: the
/// node's canonical syspath sits beneath its owning device's syspath.
/// </summary>
internal static class LinuxHidOwnership
{
    internal static bool IsOwnedBy(string hidrawDevicePath, string ownerSyspath, string sysClassHidraw = "/sys/class/hidraw")
    {
        string entry = Path.Combine(sysClassHidraw, Path.GetFileName(hidrawDevicePath));
        if (!Directory.Exists(entry))
            return false;
        // Resolve the class symlink to the node's real syspath; the class entry sits under a
        // real directory, so its relative link target resolves correctly.
        var entryInfo = new DirectoryInfo(entry);
        string resolved = (entryInfo.ResolveLinkTarget(returnFinalTarget: true) as DirectoryInfo ?? entryInfo).FullName;
        return resolved.StartsWith(ownerSyspath + "/", StringComparison.Ordinal);
    }
}
