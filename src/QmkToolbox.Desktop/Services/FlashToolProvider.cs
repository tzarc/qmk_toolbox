using System.Reflection;
using System.Runtime.Versioning;
using QmkToolbox.Core.Services;

namespace QmkToolbox.Desktop.Services;

/// <summary>
/// Extracts bundled tool binaries and data files to a local app-data folder and
/// resolves platform-appropriate tool paths.
/// </summary>
public class FlashToolProvider(string? resourceFolder = null, Assembly? resourceAssembly = null) : IFlashToolProvider
{
    private const string ResourcePrefix = "QmkToolbox.Desktop.Resources";

    // Both parameters are test seams (cf. SettingsService's path); production uses the defaults.
    private readonly Assembly _resources = resourceAssembly ?? typeof(FlashToolProvider).Assembly;
    private readonly string _resourceFolder = resourceFolder ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "QMK", "Toolbox", "Resources");

    public string GetResourceFolder() => _resourceFolder;

    public string GetToolPath(string toolName)
    {
        if (OperatingSystem.IsWindows())
            toolName += ".exe";
        return Path.Combine(GetResourceFolder(), toolName);
    }

    public string GetDataFilePath(string fileName) => Path.Combine(GetResourceFolder(), fileName);

    /// <summary>
    /// Ensures the resource folder exists and all bundled resources are present.
    /// If any installed manifest's COMMIT_DATE does not match its embedded one,
    /// the folder is wiped and fully re-extracted. Otherwise, <see cref="ExtractAllResources"/>
    /// fills any individually missing files (cheap: skips files that exist).
    /// </summary>
    public void EnsureResourceFolder()
    {
        if (!IsUpToDate(GetResourceFolder()))
            ClearResourceFolder();
        ExtractAllResources();
    }

    /// <summary>
    /// Clears the resource folder and fully re-extracts all bundled resources.
    /// </summary>
    public void ClearAndReExtract()
    {
        ClearResourceFolder();
        ExtractAllResources();
    }

    /// <summary>
    /// One line describing the installed flash utils, hidapi, and (on Linux) udev rule versions.
    /// </summary>
    public string DescribeVersions()
    {
        try
        {
            string folder = GetResourceFolder();
            (string Host, string Hash)? flash = ReadReleaseManifest(folder, "flashutils");
            (string Host, string Hash)? hidapi = ReadReleaseManifest(folder, "hidapi");
            string flashStr = flash.HasValue ? $"{flash.Value.Host}:{flash.Value.Hash}" : "unknown";
            string hidapiStr = hidapi.HasValue ? $"{hidapi.Value.Host}:{hidapi.Value.Hash}" : "unknown";
            string info = $"Flash utils: {flashStr}, hidapi: {hidapiStr}";
            if (OperatingSystem.IsLinux())
            {
                string udevStr = "unknown";
                string? installedManifest = Directory.EnumerateFiles(folder, "qmk_udev_release_*").FirstOrDefault();
                if (installedManifest != null)
                {
                    string arch = Path.GetFileName(installedManifest)["qmk_udev_release_".Length..];
                    string? version = ReadCommitDate(() => File.OpenRead(installedManifest));
                    udevStr = version != null ? $"{arch}:{version}" : "unknown";
                }
                info += $", qmk_udev: {udevStr}";
            }
            return info;
        }
        catch (Exception)
        {
            // The banner must never fail over a missing or unreadable manifest.
            return "Flash utils: unknown, hidapi: unknown";
        }
    }

    /// <summary>
    /// Returns true when every embedded release manifest's COMMIT_DATE matches its
    /// installed copy; the resource folder is then already current.
    /// </summary>
    private bool IsUpToDate(string folder) =>
        _resources.GetManifestResourceNames()
            .Where(n => n.StartsWith(ResourcePrefix + ".", StringComparison.Ordinal)
                     && n.Contains("_release_", StringComparison.Ordinal))
            .All(name =>
            {
                string installed = Path.Combine(folder, name[(ResourcePrefix.Length + 1)..]);
                if (!File.Exists(installed))
                    return false;
                string? embeddedDate = ReadCommitDate(() => _resources.GetManifestResourceStream(name));
                return embeddedDate != null && embeddedDate == ReadCommitDate(() => File.OpenRead(installed));
            });

    public static string? ReadCommitDate(Func<Stream?> openStream)
    {
        using Stream? stream = openStream();
        if (stream == null)
            return null;
        using var reader = new StreamReader(stream);
        const string prefix = "COMMIT_DATE=";
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            if (line.StartsWith(prefix, StringComparison.Ordinal))
                return line[prefix.Length..];
        }
        return null;
    }

    private void ClearResourceFolder()
    {
        string folder = GetResourceFolder();
        if (Directory.Exists(folder))
            Directory.Delete(folder, true);
    }

    private void ExtractAllResources()
    {
        Directory.CreateDirectory(GetResourceFolder());
        foreach (string name in _resources.GetManifestResourceNames()
                     .Where(n => n.StartsWith(ResourcePrefix + ".", StringComparison.Ordinal)))
        {
            string file = name[(ResourcePrefix.Length + 1)..];
            ExtractResource(file);
        }
    }

    private void ExtractResource(string file)
    {
        string destPath = Path.Combine(GetResourceFolder(), file);
        if (File.Exists(destPath))
            return;

        using Stream? stream = _resources.GetManifestResourceStream($"{ResourcePrefix}.{file}");
        if (stream == null)
            return;
        using var fileStream = new FileStream(destPath, FileMode.Create);
        stream.CopyTo(fileStream);

        if (!OperatingSystem.IsWindows() && IsExecutable(destPath))
            MakeExecutable(destPath);
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

    /// <summary>
    /// Returns true for file types that need the execute bit on Unix: tool binaries (no extension),
    /// shared libraries (.so, .so.N, .dylib), and shell scripts (.sh).
    /// Data files (.conf, .eep, .rules, .txt) and manifests (_release_*) are excluded.
    /// </summary>
    internal static bool IsExecutable(string path)
    {
        string name = Path.GetFileName(path);
        if (name.Contains("_release_", StringComparison.Ordinal))
            return false;
        // Path.GetExtension("libfoo.so.0") is ".0", so versioned shared libraries are
        // matched on the file name, not the extension.
        return Path.GetExtension(name) is "" or ".dylib" or ".sh"
            || name.EndsWith(".so", StringComparison.Ordinal)
            || name.Contains(".so.", StringComparison.Ordinal);
    }

    [UnsupportedOSPlatform("windows")]
    private static void MakeExecutable(string path)
    {
        UnixFileMode mode = File.GetUnixFileMode(path);
        File.SetUnixFileMode(path, mode
            | UnixFileMode.UserExecute
            | UnixFileMode.GroupExecute
            | UnixFileMode.OtherExecute);
    }
}
