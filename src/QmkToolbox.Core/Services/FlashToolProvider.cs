using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace QmkToolbox.Core.Services;

/// <summary>
/// Extracts bundled tool binaries and data files to a local app-data folder and
/// resolves platform-appropriate tool paths. The consuming project passes the assembly
/// that owns the embedded resources together with its resource-name prefix.
/// </summary>
public class FlashToolProvider(Assembly assembly, string resourcePrefix) : IFlashToolProvider
{
    public string GetResourceFolder() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "QMK", "Toolbox", "Resources");

    public string GetToolPath(string toolName)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && !Path.HasExtension(toolName))
            toolName += ".exe";
        return Path.Combine(GetResourceFolder(), toolName);
    }

    /// <summary>
    /// Ensures the resource folder exists and all bundled resources are present.
    /// If the installed manifest's COMMIT_DATE does not match the embedded one,
    /// the folder is wiped and fully re-extracted. Otherwise, <see cref="ExtractAllResources"/>
    /// is called to fill any individually missing files (cheap: skips files that exist).
    /// </summary>
    public void EnsureResourceFolder()
    {
        string folder = GetResourceFolder();
        Directory.CreateDirectory(folder);

        if (!IsUpToDate(folder))
        {
            Directory.Delete(folder, true);
            Directory.CreateDirectory(folder);
        }

        ExtractAllResources();
    }

    /// <summary>
    /// Returns true when the installed manifest's COMMIT_DATE matches the
    /// embedded one, meaning the resource folder is already current.
    /// </summary>
    private bool IsUpToDate(string folder)
    {
        string? manifestResourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.StartsWith(resourcePrefix + ".", StringComparison.Ordinal)
                              && n.Contains("_release_", StringComparison.Ordinal));
        if (manifestResourceName == null)
            return false;

        string manifestFile = manifestResourceName[(resourcePrefix.Length + 1)..];
        string installedManifest = Path.Combine(folder, manifestFile);
        if (!File.Exists(installedManifest))
            return false;

        string? embeddedDate = ReadCommitDate(() => assembly.GetManifestResourceStream(manifestResourceName));
        string? installedDate = ReadCommitDate(() => File.OpenRead(installedManifest));

        return embeddedDate != null && embeddedDate == installedDate;
    }

    public static string? ReadCommitDate(Func<Stream?> openStream)
    {
        using Stream? stream = openStream();
        if (stream == null)
            return null;
        using var reader = new StreamReader(stream);
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            if (line.StartsWith("COMMIT_DATE=", StringComparison.Ordinal))
                return line["COMMIT_DATE=".Length..];
        }
        return null;
    }

    public void ClearResourceFolder()
    {
        string folder = GetResourceFolder();
        if (Directory.Exists(folder))
            Directory.Delete(folder, true);
    }

    public void ExtractAllResources()
    {
        Directory.CreateDirectory(GetResourceFolder());
        foreach (string? name in assembly.GetManifestResourceNames()
                     .Where(n => n.StartsWith(resourcePrefix + ".", StringComparison.Ordinal)))
        {
            string file = name[(resourcePrefix.Length + 1)..];
            ExtractResource(file);
        }
    }

    public void ExtractResource(string file)
    {
        string destPath = Path.Combine(GetResourceFolder(), file);
        if (File.Exists(destPath))
            return;

        using Stream? stream = assembly.GetManifestResourceStream($"{resourcePrefix}.{file}");
        if (stream == null)
            return;
        using var fileStream = new FileStream(destPath, FileMode.Create);
        stream.CopyTo(fileStream);

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && IsExecutable(destPath))
            MakeExecutable(destPath);
    }

    /// <summary>
    /// Returns true for file types that need the execute bit on Unix: tool binaries (no extension),
    /// shared libraries (.so, .so.N, .dylib), and shell scripts (.sh).
    /// Data files (.conf, .eep, .rules, .txt) and manifests (_release_*) are excluded.
    /// </summary>
    private static bool IsExecutable(string path)
    {
        string ext = Path.GetExtension(path);
        return ext is "" or ".dylib" or ".sh"
            || ext.StartsWith(".so", StringComparison.Ordinal);
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
