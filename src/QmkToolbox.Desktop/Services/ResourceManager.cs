using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using QmkToolbox.Core.Bootloader;
using QmkToolbox.Core.Services;

namespace QmkToolbox.Desktop.Services;

public class ResourceManager(IFlashToolProvider toolProvider)
{
    /// <summary>
    /// Checks the embedded manifest's COMMIT_DATE against the installed copy and
    /// re-extracts all bundled resources if they differ. Called at startup before
    /// the splash messages.
    /// </summary>
    public void EnsureResources()
    {
        toolProvider.EnsureResourceFolder();
        ExtractCoreResources();
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            EnsureUdevResources();
    }

    public void ClearAndReExtractResources()
    {
        toolProvider.ClearResourceFolder();
        toolProvider.ExtractAllResources();
        ExtractCoreResources();
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            EnsureUdevResources();
    }

    public (string? FlashUtils, string? HidApi, string? UdevRules) GetManifestInfo()
    {
        string folder = toolProvider.GetResourceFolder();
        (string Host, string Hash)? flash = ReadReleaseManifest(folder, "flashutils");
        (string Host, string Hash)? hidapi = ReadReleaseManifest(folder, "hidapi");
        string? flashStr = flash.HasValue ? $"{flash.Value.Host}:{flash.Value.Hash}" : "unknown";
        string? hidapiStr = hidapi.HasValue ? $"{hidapi.Value.Host}:{hidapi.Value.Hash}" : "unknown";
        string? udevStr = null;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            string? installedManifest = Directory.EnumerateFiles(folder, "qmk_udev_release_*").FirstOrDefault();
            if (installedManifest != null)
            {
                string arch = Path.GetFileName(installedManifest)["qmk_udev_release_".Length..];
                string? version = FlashToolProvider.ReadCommitDate(() => File.OpenRead(installedManifest));
                udevStr = version != null ? $"{arch}:{version}" : "unknown";
            }
            else
            {
                udevStr = "unknown";
            }
        }
        return (flashStr, hidapiStr, udevStr);
    }

    /// <summary>
    /// Checks the embedded qmk_udev manifest COMMIT_DATE against the installed copy.
    /// Re-extracts qmk_id, 50-qmk.rules, and the manifest if the version differs or
    /// any file is missing.
    /// </summary>
    [SupportedOSPlatform("linux")]
    private void EnsureUdevResources()
    {
        const string prefix = "QmkToolbox.Desktop.Resources";
        Assembly asm = typeof(ResourceManager).Assembly;
        string folder = toolProvider.GetResourceFolder();

        // Discover the embedded per-arch manifest (e.g. qmk_udev_release_linuxX64).
        string? manifestResourceName = asm.GetManifestResourceNames()
            .FirstOrDefault(n => n.StartsWith($"{prefix}.qmk_udev_release_", StringComparison.Ordinal));
        if (manifestResourceName == null)
            return;
        string manifestName = manifestResourceName[(prefix.Length + 1)..];

        string? embeddedDate = FlashToolProvider.ReadCommitDate(() => asm.GetManifestResourceStream(manifestResourceName));
        if (embeddedDate == null)
            return;

        string installedManifestPath = Path.Combine(folder, manifestName);
        bool allPresent = File.Exists(installedManifestPath)
                       && File.Exists(Path.Combine(folder, "qmk_id"))
                       && File.Exists(Path.Combine(folder, "50-qmk.rules"));

        if (allPresent)
        {
            string? installedDate = FlashToolProvider.ReadCommitDate(() => File.OpenRead(installedManifestPath));
            if (installedDate == embeddedDate)
                return;
        }

        foreach (string file in (string[])["qmk_id", "50-qmk.rules", manifestName])
        {
            string destPath = Path.Combine(folder, file);
            using Stream? stream = asm.GetManifestResourceStream($"{prefix}.{file}");
            if (stream == null)
                continue;
            using var fs = new FileStream(destPath, FileMode.Create);
            stream.CopyTo(fs);
        }

        string qmkIdPath = Path.Combine(folder, "qmk_id");
        UnixFileMode mode = File.GetUnixFileMode(qmkIdPath);
        File.SetUnixFileMode(qmkIdPath, mode
            | UnixFileMode.UserExecute
            | UnixFileMode.GroupExecute
            | UnixFileMode.OtherExecute);
    }

    /// <summary>
    /// Extracts EEPROM reset images from the Core assembly to the resource folder.
    /// Skips files that already exist, so it is safe to call from both the startup
    /// path and the "Clear Resources" path (which deletes the folder first).
    /// </summary>
    private void ExtractCoreResources()
    {
        string folder = toolProvider.GetResourceFolder();
        Assembly coreAsm = Assembly.GetAssembly(typeof(BootloaderFactory))!;
        string[] coreFiles = ["reset.eep", "reset_left.eep", "reset_right.eep"];
        foreach (string file in coreFiles)
        {
            string destPath = Path.Combine(folder, file);
            if (File.Exists(destPath))
                continue;
            using Stream? stream = coreAsm.GetManifestResourceStream($"QmkToolbox.Core.Resources.{file}");
            if (stream == null)
                continue;
            using var fs = new FileStream(destPath, FileMode.Create);
            stream.CopyTo(fs);
        }
    }

    private static (string Host, string Hash)? ReadReleaseManifest(string folder, string prefix)
    {
        string? file = Directory.EnumerateFiles(folder, $"{prefix}_release_*").FirstOrDefault();
        if (file == null)
            return null;
        string? host = null, hash = null;
        foreach (string line in File.ReadAllLines(file))
        {
            string[] parts = line.Split('=', 2);
            if (parts.Length != 2)
                continue;
            if (parts[0].EndsWith("_HOST", StringComparison.Ordinal))
                host = parts[1];
            else if (parts[0] == "COMMIT_HASH")
                hash = parts[1];
        }
        return host != null && hash != null ? (host, hash) : null;
    }
}
