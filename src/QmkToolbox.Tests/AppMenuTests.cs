using Avalonia.Controls;
using NSubstitute;
using Qmk.Usb.Discovery;
using QmkToolbox.Core.Bootloader;
using QmkToolbox.Core.Services;
using QmkToolbox.Desktop;
using QmkToolbox.Desktop.Services;
using QmkToolbox.Desktop.ViewModels;
using Xunit;

namespace QmkToolbox.Tests;

/// <summary>
/// Covers the native menu from <see cref="AppMenu.Build"/> over a real ViewModel: top-level
/// shape, command wiring, and checkable items that track session and theme state.
/// </summary>
public sealed class AppMenuTests : IDisposable
{
    private readonly string _settingsPath = Path.Combine(Path.GetTempPath(), $"appmenu-test-{Guid.NewGuid():N}.json");
    private readonly MainWindowViewModel _vm;

    public AppMenuTests()
    {
        IFlashToolProvider toolProvider = Substitute.For<IFlashToolProvider>();
        var orchestrator = new FlashOrchestrator(new BootloaderServices(toolProvider));
        var session = new FlashSession(
            f => f(), Substitute.For<IUsbEventsDetector>(), orchestrator, toolProvider, (_, _) => { });
        _vm = new MainWindowViewModel(
            session, toolProvider, new SettingsService(_settingsPath),
            Substitute.For<IWindowService>(), _ => { }, f => f(), _ => Task.CompletedTask);
    }

    public void Dispose()
    {
        if (File.Exists(_settingsPath))
            File.Delete(_settingsPath);
    }

    private static NativeMenuItem Find(NativeMenu menu, string header)
    {
        foreach (NativeMenuItemBase entry in menu.Items)
        {
            if (entry is not NativeMenuItem item)
                continue;
            if (item.Header == header)
                return item;
            if (item.Menu != null && FindOrNull(item.Menu, header) is { } nested)
                return nested;
        }
        throw new InvalidOperationException($"Menu item '{header}' not found.");
    }

    private static NativeMenuItem? FindOrNull(NativeMenu menu, string header)
    {
        try
        {
            return Find(menu, header);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    [Fact]
    public void TopLevel_IsFileToolsHelp()
    {
        NativeMenu menu = AppMenu.Build(_vm);

        Assert.Equal(
            ["File", "Tools", "Help"],
            menu.Items.OfType<NativeMenuItem>().Select(i => i.Header));
    }

    [Fact]
    public void Items_ReferenceTheViewModelCommands()
    {
        NativeMenu menu = AppMenu.Build(_vm);

        Assert.Same(_vm.OpenFileCommand, Find(menu, "Open...").Command);
        Assert.Same(_vm.FlashCommand, Find(menu, "Flash").Command);
        Assert.Same(_vm.ResetCommand, Find(menu, "Exit DFU").Command);
        Assert.Same(_vm.ClearEepromCommand, Find(menu, "Clear EEPROM").Command);
        Assert.Same(_vm.ClearResourcesCommand, Find(menu, "Clear Resources").Command);
        Assert.Same(_vm.OpenAboutCommand, Find(menu, "About QMK Toolbox").Command);
        Assert.Same(_vm.OpenDebugLogCommand, Find(menu, "Debug Log").Command);
    }

    [Fact]
    public void AutoFlashItem_TracksSessionState()
    {
        NativeMenu menu = AppMenu.Build(_vm);
        NativeMenuItem item = Find(menu, "Auto-Flash");
        Assert.False(item.IsChecked);

        _vm.Session.AutoFlashEnabled = true;

        Assert.True(item.IsChecked);
    }

    [Fact]
    public void ShowAllDevicesItem_TracksSessionState()
    {
        NativeMenu menu = AppMenu.Build(_vm);
        NativeMenuItem item = Find(menu, "Show All Devices");

        _vm.Session.ShowAllDevices = true;

        Assert.True(item.IsChecked);
    }

    [Fact]
    public void ApplicationMenu_NonMacOS_HasAboutAndQuit()
    {
        NativeMenu menu = AppMenu.BuildApplicationMenu(_vm, isMacOS: false);

        Assert.Same(_vm.OpenAboutCommand, Find(menu, "About QMK Toolbox").Command);
        Assert.Same(_vm.ExitCommand, Find(menu, "Quit QMK Toolbox").Command);
    }

    [Fact]
    public void ApplicationMenu_MacOS_HasQuitButNoAbout()
    {
        NativeMenu menu = AppMenu.BuildApplicationMenu(_vm, isMacOS: true);

        Assert.Null(FindOrNull(menu, "About QMK Toolbox"));
        Assert.Same(_vm.ExitCommand, Find(menu, "Quit QMK Toolbox").Command);
    }

    [Fact]
    public void ThemeRadios_TrackTheSelectedVariant()
    {
        NativeMenu menu = AppMenu.Build(_vm);

        _vm.ThemeVariant = "Dark";

        Assert.True(Find(menu, "Dark").IsChecked);
        Assert.False(Find(menu, "Light").IsChecked);
        Assert.False(Find(menu, "System").IsChecked);
    }
}
