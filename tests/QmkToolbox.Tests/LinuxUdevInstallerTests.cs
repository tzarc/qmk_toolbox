using NSubstitute;
using QmkToolbox.Core.Services;
using QmkToolbox.Desktop.Services;
using Xunit;

namespace QmkToolbox.Tests;

/// <summary>
/// Tests the udev installer through the <see cref="IProcessRunner"/> seam: script generation
/// (quoting, install targets) is checked as text, and a fake runner captures the pkexec
/// invocation, so no privileged process starts.
/// </summary>
public class LinuxUdevInstallerTests
{
    // ── script generation ─────────────────────────────────────────────────────

    [Fact]
    public void BuildInstallScript_InstallsBothResourcesAndReloadsRules()
    {
        string script = LinuxUdevInstaller.BuildInstallScript("/res/qmk_id", "/res/50-qmk.rules");

        Assert.Contains("install -m 0755 '/res/qmk_id' /usr/lib/udev/qmk_id", script);
        Assert.Contains("install -m 0644 '/res/50-qmk.rules' /etc/udev/rules.d/50-qmk.rules", script);
        Assert.Contains("udevadm control --reload-rules", script);
        Assert.Contains("udevadm trigger", script);
    }

    [Fact]
    public void BuildInstallScript_QuotesShellMetacharactersInPaths()
    {
        // A path containing $(), backticks, and an apostrophe must survive as one literal.
        string hostile = "/home/it's a $(test)/`x`/qmk_id";

        string script = LinuxUdevInstaller.BuildInstallScript(hostile, "/res/50-qmk.rules");

        // Single-quote escaping: ' becomes '\'' (end quote, literal apostrophe, reopen).
        Assert.Contains("install -m 0755 '/home/it'\\''s a $(test)/`x`/qmk_id' /usr/lib/udev/qmk_id", script);
    }

    // ── pkexec invocation via the runner seam ─────────────────────────────────

    private sealed class ScriptCapturingRunner(int exitCode = 0) : IProcessRunner
    {
        public string? FileName;
        public List<string>? Args;
        public string? ScriptContent;

        public IRunningProcess Start(string fileName, string workingDir, IReadOnlyList<string> args)
        {
            FileName = fileName;
            Args = [.. args];
            // The script lives in a temp dir deleted after the run, so capture it at launch time.
            if (Args.Count > 1 && File.Exists(Args[1]))
                ScriptContent = File.ReadAllText(Args[1]);
            return new FakeRunningProcess(stdout: "Done.\n", exitCode: exitCode);
        }
    }

    private static (IFlashToolProvider Provider, string Folder) ResourceFolderWithUdevFiles()
    {
        string folder = Directory.CreateTempSubdirectory("udev-test-").FullName;
        File.WriteAllText(Path.Combine(folder, "qmk_id"), "#!/bin/sh\n");
        File.WriteAllText(Path.Combine(folder, "50-qmk.rules"), "# rules\n");
        IFlashToolProvider provider = Substitute.For<IFlashToolProvider>();
        provider.GetDataFilePath(Arg.Any<string>()).Returns(ci => Path.Combine(folder, ci.Arg<string>()));
        return (provider, folder);
    }

    [FactOnLinux]
    public async Task InstallAsync_RunsGeneratedScriptUnderPkexec_AndForwardsOutput()
    {
        (IFlashToolProvider provider, string folder) = ResourceFolderWithUdevFiles();
        var runner = new ScriptCapturingRunner();
        var output = new List<string>();
        var errors = new List<string>();

        try
        {
            await LinuxUdevInstaller.InstallAsync(provider, output.Add, errors.Add, runner);

            Assert.Equal("pkexec", runner.FileName);
            Assert.Equal("/bin/sh", runner.Args![0]);
            Assert.Equal(
                LinuxUdevInstaller.BuildInstallScript(
                    Path.Combine(folder, "qmk_id"), Path.Combine(folder, "50-qmk.rules")),
                runner.ScriptContent);
            Assert.Contains("Done.", output);
            Assert.Empty(errors);
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [FactOnLinux]
    public async Task InstallAsync_NonZeroExit_ReportsError()
    {
        (IFlashToolProvider provider, string folder) = ResourceFolderWithUdevFiles();

        try
        {
            var errors = new List<string>();
            await LinuxUdevInstaller.InstallAsync(provider, _ => { }, errors.Add, new ScriptCapturingRunner(exitCode: 127));

            Assert.Contains(errors, e => e.Contains("exit code 127"));
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [FactOnLinux]
    public async Task InstallAsync_MissingResources_ReportsErrorWithoutLaunching()
    {
        IFlashToolProvider provider = Substitute.For<IFlashToolProvider>();
        provider.GetDataFilePath(Arg.Any<string>()).Returns(ci =>
            Path.Combine(Path.GetTempPath(), "does-not-exist", ci.Arg<string>()));
        var runner = new ScriptCapturingRunner();
        var errors = new List<string>();

        await LinuxUdevInstaller.InstallAsync(provider, _ => { }, errors.Add, runner);

        Assert.Contains(errors, e => e.Contains("udev resources not found"));
        Assert.Null(runner.FileName);
    }
}
