using QmkToolbox.Desktop.Services;
using Xunit;

namespace QmkToolbox.Tests;

// xUnit v2 has no built-in conditional skip; a custom FactAttribute subclass
// sets Skip when the condition is false, making skipped tests visible in the runner
// rather than silently passing.
public class FactOnLinuxAttribute : FactAttribute
{
    public FactOnLinuxAttribute()
    {
        if (!OperatingSystem.IsLinux())
            Skip = "Linux-only test";
    }
}

public class FlashToolProviderTests
{
    private static FlashToolProvider Provider() => new();

    [Fact]
    public void GetResourceFolder_EndsWithQmkToolboxResources()
    {
        string folder = Provider().GetResourceFolder();
        Assert.EndsWith(Path.Combine("QMK", "Toolbox", "Resources"), folder);
    }

    // Exact-path pin: subsumes rooted/within-folder/contains-name/no-exe-suffix in one assertion.
    [FactOnLinux]
    public void GetToolPath_CombinesResourceFolderAndToolName()
    {
        FlashToolProvider provider = Provider();
        Assert.Equal(Path.Combine(provider.GetResourceFolder(), "avrdude"), provider.GetToolPath("avrdude"));
    }

    // Realistic names from the qmk_flashutils / qmk_udev archives.
    [Theory]
    [InlineData("avrdude", true)]                  // tool binary, no extension
    [InlineData("dfu-programmer", true)]
    [InlineData("libhidapi-hidraw.so.0", true)]    // versioned shared library
    [InlineData("libusb-1.0.so", true)]
    [InlineData("libhidapi.dylib", true)]
    [InlineData("post-install.sh", true)]
    [InlineData("avrdude.conf", false)]
    [InlineData("reset.eep", false)]
    [InlineData("50-qmk.rules", false)]
    [InlineData("mcu-list.txt", false)]
    [InlineData("flashutils_release_linuxX64", false)] // extension-less, but a manifest
    [InlineData("qmk_udev_release_linuxX64", false)]
    public void IsExecutable_ClassifiesByFileType(string fileName, bool expected) =>
        Assert.Equal(expected, FlashToolProvider.IsExecutable($"/resources/{fileName}"));
}

/// <summary>
/// Exercises extraction and staleness through a temp folder and this test assembly's embedded
/// fixture resources (flashutils/hidapi manifests plus an "avrdude" tool binary), so the real
/// extraction, manifest-comparison, and wipe paths run deterministically.
/// </summary>
public sealed class FlashToolProviderExtractionTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("qmk-resources-test-").FullName;

    private FlashToolProvider Provider() =>
        new(_folder, typeof(FlashToolProviderExtractionTests).Assembly);

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void EnsureResourceFolder_FreshFolder_ExtractsEmbeddedResources()
    {
        Provider().EnsureResourceFolder();

        Assert.True(File.Exists(Path.Combine(_folder, "avrdude")));
        Assert.True(File.Exists(Path.Combine(_folder, "flashutils_release_testhost")));
        Assert.True(File.Exists(Path.Combine(_folder, "hidapi_release_testhost")));
    }

    [Fact]
    public void GetManifestInfo_ReadsHostAndHashFromInstalledManifests()
    {
        FlashToolProvider provider = Provider();
        provider.EnsureResourceFolder();

        (string? flashUtils, string? hidApi, _) = provider.GetManifestInfo();

        Assert.Equal("testhost:cafebabe", flashUtils);
        Assert.Equal("testhost:deadbeef", hidApi);
    }

    [Fact]
    public void EnsureResourceFolder_UpToDate_FillsMissingFilesWithoutWiping()
    {
        FlashToolProvider provider = Provider();
        provider.EnsureResourceFolder();
        File.Delete(Path.Combine(_folder, "avrdude"));
        File.WriteAllText(Path.Combine(_folder, "user-file.txt"), "keep me");

        provider.EnsureResourceFolder();

        Assert.True(File.Exists(Path.Combine(_folder, "avrdude")));
        Assert.Equal("keep me", File.ReadAllText(Path.Combine(_folder, "user-file.txt")));
    }

    [Fact]
    public void EnsureResourceFolder_AnyStaleManifest_WipesAndReExtracts()
    {
        FlashToolProvider provider = Provider();
        provider.EnsureResourceFolder();
        // Stale the *second* manifest: the old single-manifest check keyed off an arbitrary
        // FirstOrDefault pick and could miss this one.
        string hidapiManifest = Path.Combine(_folder, "hidapi_release_testhost");
        File.WriteAllText(hidapiManifest, "COMMIT_DATE=1970-01-01\n");
        File.WriteAllText(Path.Combine(_folder, "user-file.txt"), "stale folder");

        provider.EnsureResourceFolder();

        Assert.False(File.Exists(Path.Combine(_folder, "user-file.txt")));
        Assert.Contains("COMMIT_DATE=2026-08-04", File.ReadAllText(hidapiManifest));
    }

    [Fact]
    public void ClearAndReExtract_RemovesForeignFilesAndRestoresResources()
    {
        FlashToolProvider provider = Provider();
        provider.EnsureResourceFolder();
        File.WriteAllText(Path.Combine(_folder, "user-file.txt"), "gone after clear");

        provider.ClearAndReExtract();

        Assert.False(File.Exists(Path.Combine(_folder, "user-file.txt")));
        Assert.True(File.Exists(Path.Combine(_folder, "avrdude")));
    }

    [FactOnLinux]
    public void EnsureResourceFolder_SetsExecuteBitOnToolBinaries()
    {
        // FactOnLinux already skips elsewhere; the guard exists for the CA1416 analyzer,
        // which can't see through the attribute.
        if (!OperatingSystem.IsLinux())
            return;

        Provider().EnsureResourceFolder();

        UnixFileMode mode = File.GetUnixFileMode(Path.Combine(_folder, "avrdude"));
        Assert.True(mode.HasFlag(UnixFileMode.UserExecute));
    }
}
