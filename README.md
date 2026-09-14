# QMK Toolbox

[![Latest Release](https://img.shields.io/github/v/release/qmk/qmk_toolbox?color=3D87CE&label=Latest&sort=semver&style=for-the-badge)](https://github.com/qmk/qmk_toolbox/releases/latest)
[![GitHub Workflow Status](https://img.shields.io/github/actions/workflow/status/qmk/qmk_toolbox/build.yml?logo=github&style=for-the-badge)](https://github.com/qmk/qmk_toolbox/actions?query=workflow%3ACI+branch%3Amaster)
[![Discord](https://img.shields.io/discord/440868230475677696.svg?logo=discord&logoColor=white&color=7289DA&style=for-the-badge)](https://discord.gg/qmk)

QMK Toolbox packages the QMK flashing tools into one app. It detects a keyboard when it enters its bootloader, and with Auto-Flash enabled it writes the selected firmware immediately.

|Windows|macOS|Linux|
|-------|-----|-----|
|[![Windows](https://i.imgur.com/jHaX9bV.png)](https://i.imgur.com/jHaX9bV.png)|[![macOS](https://i.imgur.com/8hZEfDD.png)](https://i.imgur.com/8hZEfDD.png)|![Linux](https://i.imgur.com/8hZEfDD.png)|

## Flashing

QMK Toolbox supports the following bootloaders:

 - ARM DFU (APM32, AT32, Kiibohd, STM32, STM32duino) via [dfu-util](http://dfu-util.sourceforge.net/)
 - Atmel SAM-BA (Massdrop) via [Massdrop Loader](https://github.com/massdrop/mdloader)
 - Atmel/LUFA/QMK DFU via [dfu-programmer](http://dfu-programmer.github.io/)
 - BootloadHID (Atmel, PS2AVRGB) via [bootloadHID](https://www.obdev.at/products/vusb/bootloadhid.html)
 - Caterina (Arduino, Pro Micro) via [avrdude](http://nongnu.org/avrdude/)
 - HalfKay (Teensy, Ergodox EZ) via [Teensy Loader](https://pjrc.com/teensy/loader_cli.html)
 - LUFA Mass Storage
 - LUFA/QMK HID via [hid_bootloader_cli](https://github.com/abcminiuser/lufa)
 - Raspberry Pi RP2040/RP2350 (BOOTSEL) via [picotool](https://github.com/raspberrypi/picotool)
 - RISC-V DFU (GD32V) via [dfu-util](http://dfu-util.sourceforge.net/)
 - [UF2](https://github.com/microsoft/uf2) Mass Storage
 - WB32 DFU via [wb32-dfu-updater_cli](https://github.com/WestberryTech/wb32-dfu-updater)

And the following ISP flashers:

 - AVRISP (Arduino ISP)
 - USBasp (AVR ISP)
 - USBTiny (AVR Pocket)

Other bootloaders and flashers can be added if their commands are known.

## HID Console

The Toolbox also reads HID messages on usage page `0xFF31` and usage `0x0074`, the same page and usage PJRC's [`hid_listen`](https://www.pjrc.com/teensy/hid_listen.html) uses.

With `CONSOLE_ENABLE = yes` in your keyboard's `rules.mk`, `xprintf()` prints to that console:

![Hello world from Console](https://i.imgur.com/bY8l233.png)

See the [QMK Docs](https://docs.qmk.fm/#/newbs_testing_debugging?id=debugging) for more information.

## Installation

### System requirements

* Windows 10 May 2020 Update (20H1) or higher
* macOS 13 (Ventura) or higher, on Apple Silicon or Intel
* Linux (x86_64, aarch64/arm64)

### Dependencies

On Windows, QMK Toolbox offers to install the bootloader drivers the first time it runs.

If flashing fails with "Device not found", use [Zadig](https://docs.qmk.fm/#/driver_installation_zadig) to assign the correct driver to the device.

On Linux, QMK Toolbox offers to install udev rules the first time it runs. The rules grant unprivileged access to QMK-supported bootloaders and HID devices. You can install them later from the Tools menu.

### Download

* **Windows x64:** [qmk_toolbox_install.exe](https://github.com/qmk/qmk_toolbox/releases/latest/download/qmk_toolbox_install.exe)
* **macOS (Universal):** [qmk_toolbox-macOS.dmg](https://github.com/qmk/qmk_toolbox/releases/latest/download/qmk_toolbox-macOS.dmg)
* **Linux (x86_64):** [qmk_toolbox-linux-x64](https://github.com/qmk/qmk_toolbox/releases/latest/download/qmk_toolbox-linux-x64)
* **Linux (aarch64/arm64):** [qmk_toolbox-linux-arm64](https://github.com/qmk/qmk_toolbox/releases/latest/download/qmk_toolbox-linux-arm64)

### Building from source

Building a release runs three scripts. `fetch-tools.sh` needs `curl`, `jq`, `zstd` and `tar` on the host; the other two run their toolchains in Docker.

```sh
# 1. Download flash tool binaries, hidapi, and udev resources for all platforms
scripts/fetch-tools.sh

# 2. Compile and publish self-contained executables for all targets
scripts/publish-all.sh

# 3. Assemble release artifacts (calls make-macos-app.sh, make-macos-dmg.sh and
#    make-win-installer.sh internally)
#    Outputs: artifacts/qmk_toolbox-linux-x64, qmk_toolbox-linux-arm64,
#             qmk_toolbox.exe, qmk_toolbox_install.exe,
#             qmk_toolbox-macOS.app.zip, qmk_toolbox-macOS.dmg
scripts/make-release-artifacts.sh
```

To build a single target, pass its RID to `publish-all.sh`:

```sh
scripts/publish-all.sh linux-x64
```

To build without Docker, install the [.NET 10 SDK](https://dotnet.microsoft.com/download) and run:

```sh
dotnet tool restore && dotnet husky install
dotnet build QmkToolbox.slnx
dotnet publish src/QmkToolbox.Desktop/QmkToolbox.Desktop.csproj -c Release -r <rid> --self-contained true -p:PublishSingleFile=true
```

Where `<rid>` is `win-x64`, `osx-arm64`, `osx-x64`, `linux-x64`, or `linux-arm64`.

### Updating dependencies

```sh
# Report outdated NuGet packages and dotnet tools
scripts/check-deps.sh

# Upgrade Directory.Packages.props and dotnet-tools.json in place
scripts/check-deps.sh --upgrade
```

## Attributions

### Fonts

**Inter**  
Copyright © 2016-2024 The Inter Project Authors.  
Designed by Rasmus Andersson.  
Source: https://github.com/rsms/inter  
License: [SIL Open Font License 1.1](https://openfontlicense.org/)

**JetBrains Mono**  
Copyright © 2020 JetBrains s.r.o.  
Source: https://www.jetbrains.com/lp/mono/  
License: [SIL Open Font License 1.1](https://openfontlicense.org/)
