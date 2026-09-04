using NSubstitute;
using Qmk.Usb.Discovery;
using QmkToolbox.Core.Bootloader;
using QmkToolbox.Core.Models;
using QmkToolbox.Core.Services;
using Xunit;

namespace QmkToolbox.Tests;

/// <summary>
/// Verifies the CLI command strings produced by each bootloader device class.
///
/// Strategy: a <see cref="CapturingProcessRunner"/> in the device's BootloaderServices
/// records every launched command ("{tool} {args}") without forking a child process;
/// GetToolPath resolves to the bare tool name so the captured line matches the invocation.
/// </summary>
public class FlashCommandTests
{
    // ── helpers ───────────────────────────────────────────────────────────────

    private static UsbDeviceInfo Usb(ushort vid, ushort pid, ushort rev = 0) =>
        new(vid, pid, rev, "", "", "", "");

    private static IFlashToolProvider MockToolProvider()
    {
        IFlashToolProvider p = Substitute.For<IFlashToolProvider>();
        p.GetToolPath(Arg.Any<string>()).Returns(ci => ci.Arg<string>());
        p.GetResourceFolder().Returns(Path.GetTempPath());
        return p;
    }

    private static BootloaderServices Services(
        ISerialPortService? serial = null,
        IMountPointService? mounts = null,
        IProcessRunner? runner = null) =>
        new(MockToolProvider())
        {
            ProcessRunner = runner ?? new CapturingProcessRunner(),
            SerialPorts = serial,
            MountPoints = mounts,
        };

    private static ISerialPortService MockSerialPort()
    {
        ISerialPortService s = Substitute.For<ISerialPortService>();
        s.FindSerialPort(Arg.Any<UsbDeviceInfo>()).Returns("ttyACM0");
        return s;
    }

    private static ISerialPortService MockNoSerialPort()
    {
        // NSubstitute auto-returns "" for strings; the poll must see null ("no port yet").
        ISerialPortService s = Substitute.For<ISerialPortService>();
        s.FindSerialPort(Arg.Any<UsbDeviceInfo>()).Returns((string?)null);
        return s;
    }

    /// <summary>Creates a device via the factory and collects the commands its runner launched.</summary>
    private static async Task<List<string>> Commands(
        UsbDeviceInfo usb,
        ISerialPortService? serial,
        Func<BootloaderDevice, Task> action)
    {
        var runner = new CapturingProcessRunner();
        BootloaderDevice bd = BootloaderFactory.CreateDevice(usb, Services(serial, runner: runner))!;
        await action(bd);
        return runner.Commands;
    }

    // ── AtmelDfuDevice ────────────────────────────────────────────────────────

    [Fact]
    public async Task AtmelDfuDevice_Flash_ThreeSequentialCommands()
    {
        List<string> cmds = await Commands(
            Usb(0x03EB, 0x2FEF, 0), null,
            bd => bd.FlashAsync("at90usb1286", "test.hex"));

        Assert.Equal(3, cmds.Count);
        Assert.Equal("dfu-programmer at90usb1286 erase --force", cmds[0]);
        Assert.Equal("dfu-programmer at90usb1286 flash --force test.hex", cmds[1]);
        Assert.Equal("dfu-programmer at90usb1286 reset", cmds[2]);
    }

    [Fact]
    public async Task AtmelDfuDevice_FlashEeprom_IncludesErase()
    {
        List<string> cmds = await Commands(
            Usb(0x03EB, 0x2FEF, 0), null,
            bd => bd.FlashEepromAsync("at90usb1286", "reset.eep"));

        Assert.Equal(2, cmds.Count);
        Assert.Equal("dfu-programmer at90usb1286 erase --force", cmds[0]);
        Assert.Equal("dfu-programmer at90usb1286 flash --force --suppress-validation --eeprom reset.eep", cmds[1]);
    }

    [Fact]
    public async Task QmkDfuDevice_FlashEeprom_NoErase()
    {
        List<string> cmds = await Commands(
            Usb(0x03EB, 0x2FEF, 0x0936), null,
            bd => bd.FlashEepromAsync("at90usb1286", "reset.eep"));

        Assert.Single(cmds);
        Assert.Equal("dfu-programmer at90usb1286 flash --force --suppress-validation --eeprom reset.eep", cmds[0]);
    }

    [Fact]
    public async Task AtmelDfuDevice_FlashEeprom_RejectsUnsupportedFormat()
    {
        BootloaderDevice bd = BootloaderFactory.CreateDevice(Usb(0x03EB, 0x2FEF, 0), Services())!;

        UnsupportedFileFormatException ex = await Assert.ThrowsAsync<UnsupportedFileFormatException>(() => bd.FlashEepromAsync("at90usb1286", "firmware.uf2"));
        Assert.Contains(".eep", ex.Message);
    }

    [Fact]
    public async Task AtmelDfuDevice_Reset()
    {
        List<string> cmds = await Commands(
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
    public async Task DfuUtilDevice_Flash_Bin(ushort vid, ushort pid, string deviceId)
    {
        List<string> cmds = await Commands(
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
    public async Task DfuUtilDevice_Flash_NonBin_IsRejected(ushort vid, ushort pid)
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
    public async Task DfuUtilDevice_Reset(ushort vid, ushort pid, string deviceId)
    {
        List<string> cmds = await Commands(
            Usb(vid, pid), null,
            bd => bd.ResetAsync(""));

        Assert.Single(cmds);
        Assert.Equal($"dfu-util -a 0 -d {deviceId} -s 0x08000000:leave", cmds[0]);
    }

    // ── AtmelSamBaDevice ──────────────────────────────────────────────────────

    [Fact]
    public async Task AtmelSamBaDevice_Flash()
    {
        List<string> cmds = await Commands(
            Usb(0x03EB, 0x6124), MockSerialPort(),
            bd => bd.FlashAsync("", "test.bin"));

        Assert.Single(cmds);
        Assert.Equal("mdloader -p ttyACM0 -D test.bin --restart", cmds[0]);
    }

    [Fact]
    public async Task AtmelSamBaDevice_Reset()
    {
        List<string> cmds = await Commands(
            Usb(0x03EB, 0x6124), MockSerialPort(),
            bd => bd.ResetAsync(""));

        Assert.Single(cmds);
        Assert.Equal("mdloader -p ttyACM0 --restart", cmds[0]);
    }

    [Fact]
    public async Task AtmelSamBaDevice_Flash_PortNeverAppears_ExhaustsRetriesAndThrows()
    {
        BootloaderDevice bd = BootloaderFactory.CreateDevice(Usb(0x03EB, 0x6124), Services(serial: MockNoSerialPort()))!;
        bd.PollDelayMs = 1;
        await Assert.ThrowsAsync<ComPortNotFoundException>(() => bd.FlashAsync("", "test.bin"));
    }

    // ── AvrIspDevice ──────────────────────────────────────────────────────────

    [Fact]
    public async Task AvrIspDevice_Flash()
    {
        List<string> cmds = await Commands(
            Usb(0x16C0, 0x0483), MockSerialPort(),
            bd => bd.FlashAsync("atmega32u4", "test.hex"));

        Assert.Single(cmds);
        Assert.Equal("avrdude -p atmega32u4 -c avrisp -U flash:w:test.hex:i -P ttyACM0", cmds[0]);
    }

    // ── BootloadHidDevice ─────────────────────────────────────────────────────

    [Fact]
    public async Task BootloadHidDevice_Flash_RejectsUnsupportedFormat()
    {
        BootloaderDevice bd = BootloaderFactory.CreateDevice(Usb(0x16C0, 0x05DF), Services())!;

        UnsupportedFileFormatException ex = await Assert.ThrowsAsync<UnsupportedFileFormatException>(() => bd.FlashAsync("", "test.uf2"));
        Assert.Contains(".hex", ex.Message);
    }

    [Fact]
    public async Task BootloadHidDevice_Flash()
    {
        List<string> cmds = await Commands(
            Usb(0x16C0, 0x05DF), null,
            bd => bd.FlashAsync("", "test.hex"));

        Assert.Single(cmds);
        Assert.Equal("bootloadHID -r test.hex", cmds[0]);
    }

    [Fact]
    public async Task BootloadHidDevice_Reset()
    {
        List<string> cmds = await Commands(
            Usb(0x16C0, 0x05DF), null,
            bd => bd.ResetAsync(""));

        Assert.Single(cmds);
        Assert.Equal("bootloadHID -r", cmds[0]);
    }

    // ── CaterinaDevice ────────────────────────────────────────────────────────

    [Fact]
    public async Task CaterinaDevice_Flash()
    {
        List<string> cmds = await Commands(
            Usb(0x1209, 0x2302), MockSerialPort(),
            bd => bd.FlashAsync("atmega32u4", "test.hex"));

        Assert.Single(cmds);
        Assert.Equal("avrdude -p atmega32u4 -c avr109 -U flash:w:test.hex:i -P ttyACM0", cmds[0]);
    }

    [Fact]
    public async Task CaterinaDevice_FlashEeprom()
    {
        List<string> cmds = await Commands(
            Usb(0x1209, 0x2302), MockSerialPort(),
            bd => bd.FlashEepromAsync("atmega32u4", "reset.eep"));

        Assert.Single(cmds);
        Assert.Equal("avrdude -p atmega32u4 -c avr109 -U eeprom:w:reset.eep:i -P ttyACM0", cmds[0]);
    }

    [Fact]
    public async Task CaterinaDevice_FlashEeprom_RejectsUnsupportedFormat()
    {
        BootloaderDevice bd = BootloaderFactory.CreateDevice(Usb(0x1209, 0x2302), Services(serial: MockSerialPort()))!;

        UnsupportedFileFormatException ex = await Assert.ThrowsAsync<UnsupportedFileFormatException>(() => bd.FlashEepromAsync("atmega32u4", "firmware.uf2"));
        Assert.Contains(".eep", ex.Message);
    }

    [Fact]
    public async Task CaterinaDevice_Flash_PortNeverAppears_ExhaustsRetriesAndThrows()
    {
        BootloaderDevice bd = BootloaderFactory.CreateDevice(Usb(0x1209, 0x2302), Services(serial: MockNoSerialPort()))!;
        bd.PollDelayMs = 1;
        await Assert.ThrowsAsync<ComPortNotFoundException>(() => bd.FlashAsync("atmega32u4", "test.hex"));
    }

    // ── HalfKayDevice ─────────────────────────────────────────────────────────

    [Fact]
    public async Task HalfKayDevice_Flash()
    {
        List<string> cmds = await Commands(
            Usb(0x16C0, 0x0478), null,
            bd => bd.FlashAsync("at90usb1286", "test.hex"));

        Assert.Single(cmds);
        Assert.Equal("teensy_loader_cli -mmcu=at90usb1286 test.hex -v", cmds[0]);
    }

    [Fact]
    public async Task HalfKayDevice_Reset()
    {
        List<string> cmds = await Commands(
            Usb(0x16C0, 0x0478), null,
            bd => bd.ResetAsync("at90usb1286"));

        Assert.Single(cmds);
        Assert.Equal("teensy_loader_cli -mmcu=at90usb1286 -bv", cmds[0]);
    }

    // ── KiibohdDfuDevice ──────────────────────────────────────────────────────

    [Fact]
    public async Task KiibohdDfuDevice_Flash_Bin()
    {
        List<string> cmds = await Commands(
            Usb(0x1C11, 0xB007), null,
            bd => bd.FlashAsync("", "test.bin"));

        Assert.Single(cmds);
        Assert.Equal("dfu-util -a 0 -d 1C11:B007 -D test.bin", cmds[0]);
    }

    [Fact]
    public async Task KiibohdDfuDevice_Reset()
    {
        List<string> cmds = await Commands(
            Usb(0x1C11, 0xB007), null,
            bd => bd.ResetAsync(""));

        Assert.Single(cmds);
        Assert.Equal("dfu-util -a 0 -d 1C11:B007 -e", cmds[0]);
    }

    // ── LufaHidDevice ─────────────────────────────────────────────────────────

    [Fact]
    public async Task LufaHidDevice_Flash()
    {
        List<string> cmds = await Commands(
            Usb(0x03EB, 0x2067, 0), null,
            bd => bd.FlashAsync("atmega32u4", "test.hex"));

        Assert.Single(cmds);
        Assert.Equal("hid_bootloader_cli -mmcu=atmega32u4 test.hex -v", cmds[0]);
    }

    // ── LufaMsDevice ──────────────────────────────────────────────────────────

    [Fact]
    public async Task LufaMsDevice_Flash_CopiesFileToMountPoint()
    {
        string mountDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(mountDir);
        string src = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.bin");
        File.WriteAllBytes(src, [0x01, 0x02, 0x03]);

        try
        {
            IMountPointService mount = Substitute.For<IMountPointService>();
            mount.FindMountPoint(Arg.Any<UsbDeviceInfo>(), Arg.Any<string>()).Returns(mountDir);

            BootloaderDevice bd = BootloaderFactory.CreateDevice(
                Usb(0x03EB, 0x2045), Services(mounts: mount))!;

            await bd.FlashAsync("", src);

            string dest = Path.Combine(mountDir, "FLASH.BIN");
            Assert.True(File.Exists(dest));
            Assert.Equal(File.ReadAllBytes(src), File.ReadAllBytes(dest));
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
    public async Task LufaMsDevice_Flash_MountAppearsAfterConnect_RetriesAndSucceeds()
    {
        string mountDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(mountDir);
        string src = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.bin");
        File.WriteAllBytes(src, [0x01, 0x02, 0x03]);

        try
        {
            // Automount completes after the arrival event: the first resolution attempt
            // finds nothing, the retry finds the volume.
            IMountPointService mount = Substitute.For<IMountPointService>();
            mount.FindMountPoint(Arg.Any<UsbDeviceInfo>(), Arg.Any<string>()).Returns(null, mountDir);

            BootloaderDevice bd = BootloaderFactory.CreateDevice(
                Usb(0x03EB, 0x2045), Services(mounts: mount))!;

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
    public async Task LufaMsDevice_Flash_VolumeNeverMounts_ExhaustsRetriesAndReportsError()
    {
        // The service is present but the volume never appears, so the full retry loop runs dry.
        IMountPointService mount = Substitute.For<IMountPointService>();
        mount.FindMountPoint(Arg.Any<UsbDeviceInfo>(), Arg.Any<string>()).Returns((string?)null);
        BootloaderDevice bd = BootloaderFactory.CreateDevice(
            Usb(0x03EB, 0x2045), Services(mounts: mount))!;
        bd.PollDelayMs = 1;
        var errors = new List<string>();
        bd.OutputReceived += (_, data, type) => { if (type == MessageType.Error) errors.Add(data); };

        await bd.FlashAsync("", "firmware.bin");

        mount.Received(10).FindMountPoint(Arg.Any<UsbDeviceInfo>(), Arg.Any<string>());
        Assert.Contains(errors, e => e.StartsWith("Mount point not found!"));
    }

    [Fact]
    public async Task LufaMsDevice_Flash_RejectsNonBinFile()
    {
        BootloaderDevice bd = BootloaderFactory.CreateDevice(
            Usb(0x03EB, 0x2045), Services())!;

        UnsupportedFileFormatException ex = await Assert.ThrowsAsync<UnsupportedFileFormatException>(() => bd.FlashAsync("", "firmware.hex"));
        Assert.Contains(".bin", ex.Message);
    }

    // ── Uf2Device (VID/PID arbitrary: UF2 devices are matched by marker, not ID) ──

    [Fact]
    public async Task Uf2Device_Flash_CopiesFileToVolume()
    {
        string mountDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(mountDir);
        string src = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.uf2");
        File.WriteAllBytes(src, [0x55, 0x46, 0x32, 0x0A]);

        try
        {
            IMountPointService mount = Substitute.For<IMountPointService>();
            mount.FindMountPoint(Arg.Any<UsbDeviceInfo>(), Arg.Any<string>()).Returns(mountDir);

            BootloaderDevice bd = BootloaderFactory.CreateMassStorageDevice(
                BootloaderType.Uf2, Usb(0x239A, 0x00FF), Services(mounts: mount));

            await bd.FlashAsync("", src);

            string dest = Path.Combine(mountDir, "NEW.UF2");
            Assert.True(File.Exists(dest));
            Assert.Equal(File.ReadAllBytes(src), File.ReadAllBytes(dest));
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
    public async Task Uf2Device_Flash_VolumeNeverMounts_ReportsError()
    {
        IMountPointService mount = Substitute.For<IMountPointService>();
        mount.FindMountPoint(Arg.Any<UsbDeviceInfo>(), Arg.Any<string>()).Returns((string?)null);
        BootloaderDevice bd = BootloaderFactory.CreateMassStorageDevice(
            BootloaderType.Uf2, Usb(0x239A, 0x00FF), Services(mounts: mount));
        bd.PollDelayMs = 1;
        var errors = new List<string>();
        bd.OutputReceived += (_, data, type) => { if (type == MessageType.Error) errors.Add(data); };

        await bd.FlashAsync("", "firmware.uf2");

        Assert.Contains(errors, e => e.StartsWith("Mount point not found!"));
    }

    [Fact]
    public async Task Uf2Device_Flash_RejectsNonUf2File()
    {
        BootloaderDevice bd = BootloaderFactory.CreateMassStorageDevice(
            BootloaderType.Uf2, Usb(0x239A, 0x00FF), Services());

        UnsupportedFileFormatException ex = await Assert.ThrowsAsync<UnsupportedFileFormatException>(() => bd.FlashAsync("", "firmware.bin"));
        Assert.Contains(".uf2", ex.Message);
    }

    // ── Stm32DuinoDevice ──────────────────────────────────────────────────────

    [Fact]
    public async Task Stm32DuinoDevice_Flash_Bin()
    {
        List<string> cmds = await Commands(
            Usb(0x1EAF, 0x0003), null,
            bd => bd.FlashAsync("", "test.bin"));

        Assert.Single(cmds);
        Assert.Equal("dfu-util -a 2 -d 1EAF:0003 -R -D test.bin", cmds[0]);
    }

    // ── avrdude ISP flashers (USBasp, USBTiny) ────────────────────────────────

    [Theory]
    [InlineData(0x16C0, 0x05DC, "usbasp")]  // UsbAsp (Van Ooijen)
    [InlineData(0x1781, 0x0C9F, "usbtiny")] // UsbTinyIsp (MECANIQUE)
    public async Task AvrdudeIspDevice_Flash(ushort vid, ushort pid, string programmer)
    {
        List<string> cmds = await Commands(
            Usb(vid, pid), null,
            bd => bd.FlashAsync("atmega32u4", "test.hex"));

        Assert.Single(cmds);
        Assert.Equal($"avrdude -p atmega32u4 -c {programmer} -U flash:w:test.hex:i", cmds[0]);
    }

    [Theory]
    [InlineData(0x16C0, 0x05DC, "usbasp")]  // UsbAsp (Van Ooijen)
    [InlineData(0x1781, 0x0C9F, "usbtiny")] // UsbTinyIsp (MECANIQUE)
    public async Task AvrdudeIspDevice_FlashEeprom(ushort vid, ushort pid, string programmer)
    {
        List<string> cmds = await Commands(
            Usb(vid, pid), null,
            bd => bd.FlashEepromAsync("atmega32u4", "reset.eep"));

        Assert.Single(cmds);
        Assert.Equal($"avrdude -p atmega32u4 -c {programmer} -U eeprom:w:reset.eep:i", cmds[0]);
    }

    // ── PicotoolDevice ────────────────────────────────────────────────────────

    [Theory]
    [InlineData("test.uf2")]
    [InlineData("test.bin")]
    public async Task PicotoolDevice_Flash_AcceptedFormats(string filename)
    {
        List<string> cmds = await Commands(
            Usb(0x2E8A, 0x0003), null,
            bd => bd.FlashAsync("", filename));

        Assert.Equal(2, cmds.Count);
        Assert.Equal($"picotool load {filename}", cmds[0]);
        Assert.Equal("picotool reboot", cmds[1]);
    }

    [Fact]
    public async Task PicotoolDevice_Flash_RejectsHex()
    {
        BootloaderDevice bd = BootloaderFactory.CreateDevice(Usb(0x2E8A, 0x0003), Services())!;

        UnsupportedFileFormatException ex = await Assert.ThrowsAsync<UnsupportedFileFormatException>(() => bd.FlashAsync("", "test.hex"));
        Assert.Contains(".uf2", ex.Message);
    }

    [Fact]
    public async Task PicotoolDevice_Reset()
    {
        List<string> cmds = await Commands(
            Usb(0x2E8A, 0x0003), null,
            bd => bd.ResetAsync(""));

        Assert.Single(cmds);
        Assert.Equal("picotool reboot", cmds[0]);
    }

    // ── Wb32DfuDevice ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Wb32DfuDevice_Flash_RejectsUnsupportedFormat()
    {
        BootloaderDevice bd = BootloaderFactory.CreateDevice(Usb(0x342D, 0xDFA0), Services())!;

        UnsupportedFileFormatException ex = await Assert.ThrowsAsync<UnsupportedFileFormatException>(() => bd.FlashAsync("", "test.uf2"));
        Assert.Contains(".bin", ex.Message);
    }

    [Fact]
    public async Task Wb32DfuDevice_Flash_Bin()
    {
        List<string> cmds = await Commands(
            Usb(0x342D, 0xDFA0), null,
            bd => bd.FlashAsync("", "test.bin"));

        Assert.Single(cmds);
        Assert.Equal("wb32-dfu-updater_cli --toolbox-mode --dfuse-address 0x08000000 --download test.bin", cmds[0]);
    }

    [Fact]
    public async Task Wb32DfuDevice_Flash_Hex()
    {
        List<string> cmds = await Commands(
            Usb(0x342D, 0xDFA0), null,
            bd => bd.FlashAsync("", "test.hex"));

        Assert.Single(cmds);
        Assert.Equal("wb32-dfu-updater_cli --toolbox-mode --download test.hex", cmds[0]);
    }

    [Fact]
    public async Task Wb32DfuDevice_Reset()
    {
        List<string> cmds = await Commands(
            Usb(0x342D, 0xDFA0), null,
            bd => bd.ResetAsync(""));

        Assert.Single(cmds);
        Assert.Equal("wb32-dfu-updater_cli --reset", cmds[0]);
    }
}
