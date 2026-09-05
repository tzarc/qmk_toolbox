using System.ComponentModel;
using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Input;
using QmkToolbox.Desktop.Services;
using QmkToolbox.Desktop.ViewModels;

namespace QmkToolbox.Desktop;

/// <summary>
/// Builds the window's native menu (File / Tools / Help). NativeMenu.Menu on a Window doesn't
/// inherit DataContext, so {Binding} in AXAML resolves to null and disables every item;
/// commands reference the ViewModel directly and checkable items track their source property
/// through its change notifications. The macOS application menu (the bold "QMK Toolbox"
/// entry) is separate; App.axaml and App.axaml.cs own it.
/// </summary>
public static class AppMenu
{
    public static NativeMenu Build(MainWindowViewModel vm)
    {
        var fileMenu = new NativeMenu
        {
            new NativeMenuItem("Open...")
            {
                Command = vm.OpenFileCommand,
                Gesture = new KeyGesture(Key.O, KeyModifiers.Meta)
            }
        };

        var eepromMenu = new NativeMenu
        {
            new NativeMenuItem("Clear EEPROM") { Command = vm.ClearEepromCommand },
            new NativeMenuItem("Set Left Hand") { Command = vm.SetLeftHandCommand },
            new NativeMenuItem("Set Right Hand") { Command = vm.SetRightHandCommand }
        };

        var themeMenu = new NativeMenu
        {
            Radio("Dark", vm, "Dark", nameof(MainWindowViewModel.IsDarkTheme), () => vm.IsDarkTheme),
            Radio("Light", vm, "Light", nameof(MainWindowViewModel.IsLightTheme), () => vm.IsLightTheme),
            Radio("System", vm, "Default", nameof(MainWindowViewModel.IsSystemTheme), () => vm.IsSystemTheme)
        };

        var toolsMenu = new NativeMenu
        {
            new NativeMenuItem("Flash") { Command = vm.FlashCommand },
            new NativeMenuItem("Exit DFU") { Command = vm.ResetCommand },
            new NativeMenuItem("EEPROM") { Menu = eepromMenu },
            new NativeMenuItemSeparator(),
            CheckBox("Auto-Flash", vm.ToggleAutoFlashCommand, vm.Session,
                nameof(FlashSession.AutoFlashEnabled), () => vm.Session.AutoFlashEnabled),
            CheckBox("Show All Devices", vm.ToggleShowAllDevicesCommand, vm.Session,
                nameof(FlashSession.ShowAllDevices), () => vm.Session.ShowAllDevices),
            new NativeMenuItemSeparator(),
            new NativeMenuItem("Key Tester") { Command = vm.OpenKeyTesterCommand },
            new NativeMenuItem("HID Console") { Command = vm.OpenHidConsoleCommand },
            new NativeMenuItemSeparator(),
            new NativeMenuItem("Clear Resources") { Command = vm.ClearResourcesCommand },
            new NativeMenuItemSeparator(),
            new NativeMenuItem("Theme") { Menu = themeMenu }
        };

        var helpMenu = new NativeMenu
        {
            new NativeMenuItem("About QMK Toolbox") { Command = vm.OpenAboutCommand },
            new NativeMenuItemSeparator(),
            new NativeMenuItem("Debug Log") { Command = vm.OpenDebugLogCommand }
        };

        return
        [
            new NativeMenuItem("File") { Menu = fileMenu },
            new NativeMenuItem("Tools") { Menu = toolsMenu },
            new NativeMenuItem("Help") { Menu = helpMenu }
        ];
    }

    /// <summary>
    /// Builds the application-level menu (the bold "QMK Toolbox" entry). On macOS the About
    /// item is skipped: the NSMenuBar takes About from the AXAML-declared NativeMenu.Menu,
    /// which loads during Initialize() and routes through AppAbout_OnClick, so a programmatic
    /// entry here would be dead.
    /// </summary>
    public static NativeMenu BuildApplicationMenu(MainWindowViewModel vm, bool isMacOS)
    {
        var appMenu = new NativeMenu();
        if (!isMacOS)
        {
            appMenu.Add(new NativeMenuItem("About QMK Toolbox") { Command = vm.OpenAboutCommand });
            appMenu.Add(new NativeMenuItemSeparator());
        }
        appMenu.Add(new NativeMenuItem("Quit QMK Toolbox")
        {
            Command = vm.ExitCommand,
            Gesture = new KeyGesture(Key.Q, KeyModifiers.Meta)
        });
        return [new NativeMenuItem("QMK Toolbox") { Menu = appMenu }];
    }

    private static NativeMenuItem CheckBox(
        string header, ICommand command, INotifyPropertyChanged source, string propertyName, Func<bool> isChecked)
    {
        var item = new NativeMenuItem(header)
        {
            Command = command,
            ToggleType = MenuItemToggleType.CheckBox,
            IsChecked = isChecked()
        };
        Track(item, source, propertyName, isChecked);
        return item;
    }

    private static NativeMenuItem Radio(
        string header, MainWindowViewModel vm, string variant, string propertyName, Func<bool> isChecked)
    {
        var item = new NativeMenuItem(header)
        {
            Command = vm.SetThemeCommand,
            CommandParameter = variant,
            ToggleType = MenuItemToggleType.Radio,
            IsChecked = isChecked()
        };
        Track(item, vm, propertyName, isChecked);
        return item;
    }

    // The subscription lives for the source's lifetime; the menu and its ViewModel are both
    // application-lifetime objects, so nothing needs to unsubscribe.
    private static void Track(
        NativeMenuItem item, INotifyPropertyChanged source, string propertyName, Func<bool> isChecked)
    {
        source.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == propertyName)
                item.IsChecked = isChecked();
        };
    }
}
