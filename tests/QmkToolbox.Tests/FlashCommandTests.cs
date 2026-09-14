using NSubstitute;
using QmkToolbox.Core.Bootloader;
using QmkToolbox.Core.Models;
using QmkToolbox.Core.Services;
using QmkToolbox.Usb.Discovery;
using Xunit;

namespace QmkToolbox.Tests;

/// <summary>
/// Verifies the CLI command strings produced by each bootloader device class.
///
/// A <see cref="CapturingProcessRunner"/> in the device's BootloaderServices records every
/// launched command ("{tool} {args}") without forking a child process; GetToolPath resolves
/// to the bare tool name so the captured line matches the invocation.
/// </summary>
public class FlashCommandTests
{
    // ── helpers ───────────────────────────────────────────────────────────────

    private static UsbDeviceInfo Usb(ushort vid, ushort pid, ushort rev = 0) =>
        new(vid, pid, rev, "", "", "");

    private static IFlashToolProvider MockToolProvider()
    {
        IFlashToolProvider p = Substitute.For<IFlashToolProvider>();
        p.GetToolPath(Arg.Any<string>()).Returns(ci => ci.Arg<string>());
        p.GetResourceFolder().Returns(Path.GetTempPath());
        return p;
    }

    private static BootloaderServices Services(
        IUsbDeviceResolver? resolver = null,
        IProcessRunner? runner = null,
        MessageSink? output = null) =>
        new(MockToolProvider(), resolver ?? new FakeUsbResolver(), output ?? ((_, _) => { }))
        {
            PollDelayMs = 1,
            ProcessRunner = runner ?? new CapturingProcessRunner(),
        };

    /// <summary>A sink that collects the device's error lines.</summary>
    private static MessageSink Errors(List<string> into) =>
        (text, type) => { if (type == MessageType.Error) into.Add(text); };

    private static FakeUsbResolver ResolverWithSerialPort()
    {
        var resolver = new FakeUsbResolver();
        resolver.SerialPorts.Add("ttyACM0");
        return resolver;
    }

    /// <summary>Creates a temp directory standing in for a mounted volume carrying <paramref name="markerFile"/>.</summary>
    private static string MarkerVolumeDir(string markerFile)
    {
        string dir = Directory.CreateTempSubdirectory("volume-").FullName;
        File.WriteAllText(Path.Combine(dir, markerFile), "");
        return dir;
    }

    /// <summary>Creates a device via the factory and collects the commands its runner launched.</summary>
    private static async Task<List<string>> CommandsAsync(
        UsbDeviceInfo usb,
        IUsbDeviceResolver? resolver,
        Func<BootloaderDevice, Task> action)
    {
        var runner = new CapturingProcessRunner();
        BootloaderDevice bd = BootloaderFactory.CreateDevice(usb, Services(resolver, runner))!;
        await action(bd);
        return runner.Commands;
    }

    // ── AtmelDfuDevice ────────────────────────────────────────────────────────

    [Fact]
    public async Task AtmelDfuDevice_Flash_ThreeSequentialCommandsAsync()
    {
        List<string> cmds = await CommandsAsync(
            Usb(0x03EB, 0x2FEF, 0), null,
            bd => bd.FlashAsync("at90usb1286", "test.hex"));

        Assert.Equal(3, cmds.Count);
        Assert.Equal("dfu-programmer at90usb1286 erase --force", cmds[0]);
        Assert.Equal("dfu-programmer at90usb1286 flash --force test.hex", cmds[1]);
        Assert.Equal("dfu-programmer at90usb1286 reset", cmds[2]);
    }

    [Fact]
    public async Task AtmelDfuDevice_FlashEeprom_IncludesEraseAsync()
    {
        List<string> cmds = await CommandsAsync(
            Usb(0x03EB, 0x2FEF, 0), null,
            bd => bd.FlashEepromAsync("at90usb1286", "reset.eep"));

        Assert.Equal(2, cmds.Count);
        Assert.Equal("dfu-programmer at90usb1286 erase --force", cmds[0]);
        Assert.Equal("dfu-programmer at90usb1286 flash --force --suppress-validation --eeprom reset.eep", cmds[1]);
    }

    [Fact]
    public async Task QmkDfuDevice_FlashEeprom_NoEraseAsync()
    {
        List<string> cmds = await CommandsAsync(
            Usb(0x03EB, 0x2FEF, 0x0936), null,
            bd => bd.FlashEepromAsync("at90usb1286", "reset.eep"));

        Assert.Single(cmds);
        Assert.Equal("dfu-programmer at90usb1286 flash --force --suppress-validation --eeprom reset.eep", cmds[0]);
    }

    [Fact]
    public async Task AtmelDfuDevice_FlashEeprom_RejectsUnsupportedFormatAsync()
    {
        BootloaderDevice bd = BootloaderFactory.CreateDevice(Usb(0x03EB, 0x2FEF, 0), Services())!;

        UnsupportedFileFormatException ex = await Assert.ThrowsAsync<UnsupportedFileFormatException>(() => bd.FlashEepromAsync("at90usb1286", "firmware.uf2"));
        Assert.Contains(".eep", ex.Message);
    }

    [Fact]
    public async Task AtmelDfuDevice_ResetAsync()
    {
        List<string> cmds = await CommandsAsync(
            Usb(0x03EB, 0x2FEF, 0), null,
            bd => bd.ResetAsync("at90usb1286"));

        Assert.Single(cmds);
        Assert.Equal("dfu-programmer at90usb1286 reset", cmds[0]);
    }

    // ── dfu-util devices (APM32, AT32, GD32V, STM32) ─────────────────────────

    [Theory]
    [InlineData(0x314B, 0x0106, "314B:0106")] // Apm32Dfu (Geehy)
    [InlineData(0x2E3C, 0xDF11, "2E3C:DF11")] // At32Dfu (ArteryTek)
    [InlineData(0x28E9, 0x0189, "28E9:0189")] // Gd32VDfu (GigaDevice)
    [InlineData(0x0483, 0xDF11, "0483:DF11")] // Stm32Dfu (STMicroelectronics)
    public async Task DfuUtilDevice_Flash_BinAsync(ushort vid, ushort pid, string deviceId)
    {
        List<string> cmds = await CommandsAsync(
            Usb(vid, pid), null,
            bd => bd.FlashAsync("", "test.bin"));

        Assert.Single(cmds);
        Assert.Equal($"dfu-util -a 0 -d {deviceId} -s 0x08000000:leave -D test.bin", cmds[0]);
    }

    [Theory]
    [InlineData(0x314B, 0x0106)] // Apm32Dfu (Geehy)
    [InlineData(0x2E3C, 0xDF11)] // At32Dfu (ArteryTek)
    [InlineData(0x28E9, 0x0189)] // Gd32VDfu (GigaDevice)
    [InlineData(0x0483, 0xDF11)] // Stm32Dfu (STMicroelectronics)
    public async Task DfuUtilDevice_Flash_NonBin_IsRejectedAsync(ushort vid, ushort pid)
    {
        BootloaderDevice bd = BootloaderFactory.CreateDevice(Usb(vid, pid), Services())!;

        UnsupportedFileFormatException ex = await Assert.ThrowsAsync<UnsupportedFileFormatException>(() => bd.FlashAsync("", "test.hex"));
        Assert.Contains(".bin", ex.Message);
    }

    [Theory]
    [InlineData(0x314B, 0x0106, "314B:0106")] // Apm32Dfu (Geehy)
    [InlineData(0x2E3C, 0xDF11, "2E3C:DF11")] // At32Dfu (ArteryTek)
    [InlineData(0x28E9, 0x0189, "28E9:0189")] // Gd32VDfu (GigaDevice)
    [InlineData(0x0483, 0xDF11, "0483:DF11")] // Stm32Dfu (STMicroelectronics)
    public async Task DfuUtilDevice_ResetAsync(ushort vid, ushort pid, string deviceId)
    {
        List<string> cmds = await CommandsAsync(
            Usb(vid, pid), null,
            bd => bd.ResetAsync(""));

        Assert.Single(cmds);
        Assert.Equal($"dfu-util -a 0 -d {deviceId} -s 0x08000000:leave", cmds[0]);
    }

    // ── AtmelSamBaDevice ──────────────────────────────────────────────────────

    [Fact]
    public async Task AtmelSamBaDevice_FlashAsync()
    {
        List<string> cmds = await CommandsAsync(
            Usb(0x03EB, 0x6124), ResolverWithSerialPort(),
            bd => bd.FlashAsync("", "test.bin"));

        Assert.Single(cmds);
        Assert.Equal("mdloader -p ttyACM0 -D test.bin --restart", cmds[0]);
    }

    [Fact]
    public async Task AtmelSamBaDevice_ResetAsync()
    {
        List<string> cmds = await CommandsAsync(
            Usb(0x03EB, 0x6124), ResolverWithSerialPort(),
            bd => bd.ResetAsync(""));

        Assert.Single(cmds);
        Assert.Equal("mdloader -p ttyACM0 --restart", cmds[0]);
    }

    [Fact]
    public async Task AtmelSamBaDevice_Flash_PortNeverAppears_ExhaustsRetriesAndThrowsAsync()
    {
        BootloaderDevice bd = BootloaderFactory.CreateDevice(Usb(0x03EB, 0x6124), Services())!;
        await Assert.ThrowsAsync<ComPortNotFoundException>(() => bd.FlashAsync("", "test.bin"));
    }

    // ── AvrIspDevice ──────────────────────────────────────────────────────────

    [Fact]
    public async Task AvrIspDevice_FlashAsync()
    {
        List<string> cmds = await CommandsAsync(
            Usb(0x16C0, 0x0483), ResolverWithSerialPort(),
            bd => bd.FlashAsync("atmega32u4", "test.hex"));

        Assert.Single(cmds);
        Assert.Equal("avrdude -p atmega32u4 -c avrisp -U flash:w:test.hex:i -P ttyACM0", cmds[0]);
    }

    // ── BootloadHidDevice ─────────────────────────────────────────────────────

    [Fact]
    public async Task BootloadHidDevice_Flash_RejectsUnsupportedFormatAsync()
    {
        BootloaderDevice bd = BootloaderFactory.CreateDevice(Usb(0x16C0, 0x05DF), Services())!;

        UnsupportedFileFormatException ex = await Assert.ThrowsAsync<UnsupportedFileFormatException>(() => bd.FlashAsync("", "test.uf2"));
        Assert.Contains(".hex", ex.Message);
    }

    [Fact]
    public async Task BootloadHidDevice_FlashAsync()
    {
        List<string> cmds = await CommandsAsync(
            Usb(0x16C0, 0x05DF), null,
            bd => bd.FlashAsync("", "test.hex"));

        Assert.Single(cmds);
        Assert.Equal("bootloadHID -r test.hex", cmds[0]);
    }

    [Fact]
    public async Task BootloadHidDevice_ResetAsync()
    {
        List<string> cmds = await CommandsAsync(
            Usb(0x16C0, 0x05DF), null,
            bd => bd.ResetAsync(""));

        Assert.Single(cmds);
        Assert.Equal("bootloadHID -r", cmds[0]);
    }

    // ── CaterinaDevice ────────────────────────────────────────────────────────

    [Fact]
    public async Task CaterinaDevice_FlashAsync()
    {
        List<string> cmds = await CommandsAsync(
            Usb(0x1209, 0x2302), ResolverWithSerialPort(),
            bd => bd.FlashAsync("atmega32u4", "test.hex"));

        Assert.Single(cmds);
        Assert.Equal("avrdude -p atmega32u4 -c avr109 -U flash:w:test.hex:i -P ttyACM0", cmds[0]);
    }

    [Fact]
    public async Task CaterinaDevice_FlashEepromAsync()
    {
        List<string> cmds = await CommandsAsync(
            Usb(0x1209, 0x2302), ResolverWithSerialPort(),
            bd => bd.FlashEepromAsync("atmega32u4", "reset.eep"));

        Assert.Single(cmds);
        Assert.Equal("avrdude -p atmega32u4 -c avr109 -U eeprom:w:reset.eep:i -P ttyACM0", cmds[0]);
    }

    [Fact]
    public async Task CaterinaDevice_FlashEeprom_RejectsUnsupportedFormatAsync()
    {
        BootloaderDevice bd = BootloaderFactory.CreateDevice(Usb(0x1209, 0x2302), Services(ResolverWithSerialPort()))!;

        UnsupportedFileFormatException ex = await Assert.ThrowsAsync<UnsupportedFileFormatException>(() => bd.FlashEepromAsync("atmega32u4", "firmware.uf2"));
        Assert.Contains(".eep", ex.Message);
    }

    [Fact]
    public async Task CaterinaDevice_Flash_PortNeverAppears_ExhaustsRetriesAndThrowsAsync()
    {
        BootloaderDevice bd = BootloaderFactory.CreateDevice(Usb(0x1209, 0x2302), Services())!;
        await Assert.ThrowsAsync<ComPortNotFoundException>(() => bd.FlashAsync("atmega32u4", "test.hex"));
    }

    // ── HalfKayDevice ─────────────────────────────────────────────────────────

    [Fact]
    public async Task HalfKayDevice_FlashAsync()
    {
        List<string> cmds = await CommandsAsync(
            Usb(0x16C0, 0x0478), null,
            bd => bd.FlashAsync("at90usb1286", "test.hex"));

        Assert.Single(cmds);
        Assert.Equal("teensy_loader_cli -mmcu=at90usb1286 test.hex -v", cmds[0]);
    }

    [Fact]
    public async Task HalfKayDevice_ResetAsync()
    {
        List<string> cmds = await CommandsAsync(
            Usb(0x16C0, 0x0478), null,
            bd => bd.ResetAsync("at90usb1286"));

        Assert.Single(cmds);
        Assert.Equal("teensy_loader_cli -mmcu=at90usb1286 -bv", cmds[0]);
    }

    // ── KiibohdDfuDevice ──────────────────────────────────────────────────────

    [Fact]
    public async Task KiibohdDfuDevice_Flash_BinAsync()
    {
        List<string> cmds = await CommandsAsync(
            Usb(0x1C11, 0xB007), null,
            bd => bd.FlashAsync("", "test.bin"));

        Assert.Single(cmds);
        Assert.Equal("dfu-util -a 0 -d 1C11:B007 -D test.bin", cmds[0]);
    }

    [Fact]
    public async Task KiibohdDfuDevice_ResetAsync()
    {
        List<string> cmds = await CommandsAsync(
            Usb(0x1C11, 0xB007), null,
            bd => bd.ResetAsync(""));

        Assert.Single(cmds);
        Assert.Equal("dfu-util -a 0 -d 1C11:B007 -e", cmds[0]);
    }

    // ── LufaHidDevice ─────────────────────────────────────────────────────────

    [Fact]
    public async Task LufaHidDevice_FlashAsync()
    {
        List<string> cmds = await CommandsAsync(
            Usb(0x03EB, 0x2067, 0), null,
            bd => bd.FlashAsync("atmega32u4", "test.hex"));

        Assert.Single(cmds);
        Assert.Equal("hid_bootloader_cli -mmcu=atmega32u4 test.hex -v", cmds[0]);
    }

    // ── LufaMsDevice ──────────────────────────────────────────────────────────

    [Fact]
    public async Task LufaMsDevice_Flash_CopiesFileToMountPointAsync()
    {
        // The LUFA MS virtual FAT ships FLASH.BIN, so the marker file and the destination the
        // firmware overwrites are the same file.
        string mountDir = MarkerVolumeDir("FLASH.BIN");
        string src = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.bin");
        File.WriteAllBytes(src, [0x01, 0x02, 0x03]);

        try
        {
            var resolver = new FakeUsbResolver();
            resolver.Volumes.Add(mountDir);

            BootloaderDevice bd = BootloaderFactory.CreateDevice(
                Usb(0x03EB, 0x2045), Services(resolver))!;

            await bd.FlashAsync("", src);

            string dest = Path.Combine(mountDir, "FLASH.BIN");
            Assert.True(File.Exists(dest));
            Assert.Equal(await File.ReadAllBytesAsync(src), await File.ReadAllBytesAsync(dest));
        }
        finally
        {
            if (Directory.Exists(mountDir))
                Directory.Delete(mountDir, true);
            if (File.Exists(src))
                File.Delete(src);
        }
    }

    [Fact]
    public async Task LufaMsDevice_Flash_MountAppearsAfterConnect_RetriesAndSucceedsAsync()
    {
        string mountDir = MarkerVolumeDir("FLASH.BIN");
        string src = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.bin");
        File.WriteAllBytes(src, [0x01, 0x02, 0x03]);

        try
        {
            // Automount completes after the arrival event: the first resolution attempt
            // finds no volume, the retry finds it.
            IUsbDeviceResolver resolver = Substitute.For<IUsbDeviceResolver>();
            resolver.EnumerateVolumes(Arg.Any<UsbDeviceInfo>()).Returns([], [mountDir]);

            BootloaderDevice bd = BootloaderFactory.CreateDevice(
                Usb(0x03EB, 0x2045), Services(resolver))!;

            await bd.FlashAsync("", src);

            Assert.True(File.Exists(Path.Combine(mountDir, "FLASH.BIN")));
        }
        finally
        {
            if (Directory.Exists(mountDir))
                Directory.Delete(mountDir, true);
            if (File.Exists(src))
                File.Delete(src);
        }
    }

    [Fact]
    public async Task LufaMsDevice_Flash_VolumeNeverMounts_ExhaustsRetriesAndReportsErrorAsync()
    {
        IUsbDeviceResolver resolver = Substitute.For<IUsbDeviceResolver>();
        resolver.EnumerateVolumes(Arg.Any<UsbDeviceInfo>()).Returns([]);
        var errors = new List<string>();
        BootloaderDevice bd = BootloaderFactory.CreateDevice(
            Usb(0x03EB, 0x2045), Services(resolver, output: Errors(errors)))!;

        await bd.FlashAsync("", "firmware.bin");

        resolver.Received(10).EnumerateVolumes(Arg.Any<UsbDeviceInfo>());
        Assert.Contains(errors, e => e.StartsWith("Mount point not found!", StringComparison.Ordinal));
    }

    [Fact]
    public async Task LufaMsDevice_Flash_RejectsNonBinFileAsync()
    {
        BootloaderDevice bd = BootloaderFactory.CreateDevice(
            Usb(0x03EB, 0x2045), Services())!;

        UnsupportedFileFormatException ex = await Assert.ThrowsAsync<UnsupportedFileFormatException>(() => bd.FlashAsync("", "firmware.hex"));
        Assert.Contains(".bin", ex.Message);
    }

    // ── Uf2Device (VID/PID arbitrary: UF2 devices match on marker file, not ID) ──

    [Fact]
    public async Task Uf2Device_Flash_CopiesFileToVolumeAsync()
    {
        string mountDir = MarkerVolumeDir("INFO_UF2.TXT");
        string src = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.uf2");
        File.WriteAllBytes(src, [0x55, 0x46, 0x32, 0x0A]);

        try
        {
            var resolver = new FakeUsbResolver();
            resolver.Volumes.Add(mountDir);

            BootloaderDevice bd = BootloaderFactory.CreateMassStorageDevice(
                BootloaderType.Uf2, Usb(0x239A, 0x00FF), Services(resolver));

            await bd.FlashAsync("", src);

            string dest = Path.Combine(mountDir, "NEW.UF2");
            Assert.True(File.Exists(dest));
            Assert.Equal(await File.ReadAllBytesAsync(src), await File.ReadAllBytesAsync(dest));
        }
        finally
        {
            if (Directory.Exists(mountDir))
                Directory.Delete(mountDir, true);
            if (File.Exists(src))
                File.Delete(src);
        }
    }

    [Fact]
    public async Task Uf2Device_Flash_VolumeNeverMounts_ReportsErrorAsync()
    {
        var errors = new List<string>();
        BootloaderDevice bd = BootloaderFactory.CreateMassStorageDevice(
            BootloaderType.Uf2, Usb(0x239A, 0x00FF), Services(output: Errors(errors)));

        await bd.FlashAsync("", "firmware.uf2");

        Assert.Contains(errors, e => e.StartsWith("Mount point not found!", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Uf2Device_Flash_RejectsNonUf2FileAsync()
    {
        BootloaderDevice bd = BootloaderFactory.CreateMassStorageDevice(
            BootloaderType.Uf2, Usb(0x239A, 0x00FF), Services());

        UnsupportedFileFormatException ex = await Assert.ThrowsAsync<UnsupportedFileFormatException>(() => bd.FlashAsync("", "firmware.bin"));
        Assert.Contains(".uf2", ex.Message);
    }

    // ── marker-volume selection ───────────────────────────────────────────────
    // A device can back several volumes at once; only the one carrying the family's marker
    // file is the bootloader's.

    [Fact]
    public async Task Uf2Device_Flash_SeveralVolumes_PicksTheOneCarryingTheMarkerAsync()
    {
        string plainVolume = Directory.CreateTempSubdirectory("volume-").FullName;
        string markerVolume = MarkerVolumeDir("INFO_UF2.TXT");
        string src = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.uf2");
        File.WriteAllBytes(src, [0x55, 0x46, 0x32, 0x0A]);

        try
        {
            var resolver = new FakeUsbResolver();
            resolver.Volumes.Add(plainVolume);
            resolver.Volumes.Add(markerVolume);

            BootloaderDevice bd = BootloaderFactory.CreateMassStorageDevice(
                BootloaderType.Uf2, Usb(0x239A, 0x00FF), Services(resolver));

            await bd.FlashAsync("", src);

            Assert.True(File.Exists(Path.Combine(markerVolume, "NEW.UF2")));
            Assert.False(File.Exists(Path.Combine(plainVolume, "NEW.UF2")));
        }
        finally
        {
            Directory.Delete(plainVolume, true);
            Directory.Delete(markerVolume, true);
            File.Delete(src);
        }
    }

    [Fact]
    public async Task Uf2Device_Flash_MountedVolumeWithoutMarker_ReportsErrorAsync()
    {
        string plainVolume = Directory.CreateTempSubdirectory("volume-").FullName;

        try
        {
            var resolver = new FakeUsbResolver();
            resolver.Volumes.Add(plainVolume);

            var errors = new List<string>();
            BootloaderDevice bd = BootloaderFactory.CreateMassStorageDevice(
                BootloaderType.Uf2, Usb(0x239A, 0x00FF), Services(resolver, output: Errors(errors)));

            await bd.FlashAsync("", "firmware.uf2");

            Assert.Contains(errors, e => e.StartsWith("Mount point not found!", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(plainVolume, true);
        }
    }

    // Windows drive roots enumerate as "E:\"; the appended separator stands in for that shape.
    [Fact]
    public async Task Uf2Device_Flash_VolumeRootWithTrailingSeparator_IsTrimmedAsync()
    {
        string markerVolume = MarkerVolumeDir("INFO_UF2.TXT");
        string src = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.uf2");
        File.WriteAllBytes(src, [0x55, 0x46, 0x32, 0x0A]);

        try
        {
            var resolver = new FakeUsbResolver();
            resolver.Volumes.Add(markerVolume + Path.DirectorySeparatorChar);

            BootloaderDevice bd = BootloaderFactory.CreateMassStorageDevice(
                BootloaderType.Uf2, Usb(0x239A, 0x00FF), Services(resolver));

            await bd.FlashAsync("", src);

            Assert.EndsWith($"[{markerVolume}]", bd.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(markerVolume, true);
            File.Delete(src);
        }
    }

    // ── Stm32DuinoDevice ──────────────────────────────────────────────────────

    [Fact]
    public async Task Stm32DuinoDevice_Flash_BinAsync()
    {
        List<string> cmds = await CommandsAsync(
            Usb(0x1EAF, 0x0003), null,
            bd => bd.FlashAsync("", "test.bin"));

        Assert.Single(cmds);
        Assert.Equal("dfu-util -a 2 -d 1EAF:0003 -R -D test.bin", cmds[0]);
    }

    // ── avrdude ISP flashers (USBasp, USBTiny) ────────────────────────────────

    [Theory]
    [InlineData(0x16C0, 0x05DC, "usbasp")]  // UsbAsp (Van Ooijen)
    [InlineData(0x1781, 0x0C9F, "usbtiny")] // UsbTinyIsp (MECANIQUE)
    public async Task AvrdudeIspDevice_FlashAsync(ushort vid, ushort pid, string programmer)
    {
        List<string> cmds = await CommandsAsync(
            Usb(vid, pid), null,
            bd => bd.FlashAsync("atmega32u4", "test.hex"));

        Assert.Single(cmds);
        Assert.Equal($"avrdude -p atmega32u4 -c {programmer} -U flash:w:test.hex:i", cmds[0]);
    }

    [Theory]
    [InlineData(0x16C0, 0x05DC, "usbasp")]  // UsbAsp (Van Ooijen)
    [InlineData(0x1781, 0x0C9F, "usbtiny")] // UsbTinyIsp (MECANIQUE)
    public async Task AvrdudeIspDevice_FlashEepromAsync(ushort vid, ushort pid, string programmer)
    {
        List<string> cmds = await CommandsAsync(
            Usb(vid, pid), null,
            bd => bd.FlashEepromAsync("atmega32u4", "reset.eep"));

        Assert.Single(cmds);
        Assert.Equal($"avrdude -p atmega32u4 -c {programmer} -U eeprom:w:reset.eep:i", cmds[0]);
    }

    // ── PicotoolDevice ────────────────────────────────────────────────────────

    [Theory]
    [InlineData("test.uf2")]
    [InlineData("test.bin")]
    public async Task PicotoolDevice_Flash_AcceptedFormatsAsync(string filename)
    {
        List<string> cmds = await CommandsAsync(
            Usb(0x2E8A, 0x0003), null,
            bd => bd.FlashAsync("", filename));

        Assert.Equal(2, cmds.Count);
        Assert.Equal($"picotool load {filename}", cmds[0]);
        Assert.Equal("picotool reboot", cmds[1]);
    }

    [Fact]
    public async Task PicotoolDevice_Flash_RejectsHexAsync()
    {
        BootloaderDevice bd = BootloaderFactory.CreateDevice(Usb(0x2E8A, 0x0003), Services())!;

        UnsupportedFileFormatException ex = await Assert.ThrowsAsync<UnsupportedFileFormatException>(() => bd.FlashAsync("", "test.hex"));
        Assert.Contains(".uf2", ex.Message);
    }

    [Fact]
    public async Task PicotoolDevice_ResetAsync()
    {
        List<string> cmds = await CommandsAsync(
            Usb(0x2E8A, 0x0003), null,
            bd => bd.ResetAsync(""));

        Assert.Single(cmds);
        Assert.Equal("picotool reboot", cmds[0]);
    }

    // ── Wb32DfuDevice ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Wb32DfuDevice_Flash_RejectsUnsupportedFormatAsync()
    {
        BootloaderDevice bd = BootloaderFactory.CreateDevice(Usb(0x342D, 0xDFA0), Services())!;

        UnsupportedFileFormatException ex = await Assert.ThrowsAsync<UnsupportedFileFormatException>(() => bd.FlashAsync("", "test.uf2"));
        Assert.Contains(".bin", ex.Message);
    }

    [Fact]
    public async Task Wb32DfuDevice_Flash_BinAsync()
    {
        List<string> cmds = await CommandsAsync(
            Usb(0x342D, 0xDFA0), null,
            bd => bd.FlashAsync("", "test.bin"));

        Assert.Single(cmds);
        Assert.Equal("wb32-dfu-updater_cli --toolbox-mode --dfuse-address 0x08000000 --download test.bin", cmds[0]);
    }

    [Fact]
    public async Task Wb32DfuDevice_Flash_HexAsync()
    {
        List<string> cmds = await CommandsAsync(
            Usb(0x342D, 0xDFA0), null,
            bd => bd.FlashAsync("", "test.hex"));

        Assert.Single(cmds);
        Assert.Equal("wb32-dfu-updater_cli --toolbox-mode --download test.hex", cmds[0]);
    }

    [Fact]
    public async Task Wb32DfuDevice_ResetAsync()
    {
        List<string> cmds = await CommandsAsync(
            Usb(0x342D, 0xDFA0), null,
            bd => bd.ResetAsync(""));

        Assert.Single(cmds);
        Assert.Equal("wb32-dfu-updater_cli --reset", cmds[0]);
    }
}
