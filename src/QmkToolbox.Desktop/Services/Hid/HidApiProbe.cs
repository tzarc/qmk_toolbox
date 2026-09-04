using HidApi;

namespace QmkToolbox.Desktop.Services.Hid;

/// <summary>
/// Production probe over HidApi.Net. hidapi has no hotplug callbacks, so discovery is
/// enumeration-based; <see cref="Start"/> and <see cref="Dispose"/> bracket
/// <c>Hid.Init</c>/<c>Hid.Exit</c>.
/// </summary>
internal sealed class HidApiProbe : IHidProbe
{
    // Open needs the full DeviceInfo behind a key; kept from the latest enumeration.
    private readonly Dictionary<HidDeviceKey, DeviceInfo> _lastSeen = [];

    public void Start() => HidApi.Hid.Init();

    public IReadOnlyList<HidDeviceKey> EnumerateKeys()
    {
        _lastSeen.Clear();
        foreach (DeviceInfo info in HidApi.Hid.Enumerate().Where(HidConsoleDevice.Match))
            _lastSeen[new HidDeviceKey(info.Path, info.UsagePage, info.Usage)] = info;
        return [.. _lastSeen.Keys];
    }

    public BaseHidDevice? Open(HidDeviceKey key) =>
        _lastSeen.TryGetValue(key, out DeviceInfo? info) ? HidConsoleDevice.TryCreate(info) : null;

    public void Dispose() => HidApi.Hid.Exit();
}
