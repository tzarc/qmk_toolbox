using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QmkToolbox.Core.Models;
using QmkToolbox.Core.Services;
using QmkToolbox.Desktop.ViewModels;
using QmkToolbox.Usb.Discovery;

namespace QmkToolbox.Desktop.Services;

/// <summary>
/// The flashing workflow: firmware selection and history, MCU choice, auto-flash policy, and the
/// readiness flags derived from the tracked bootloaders. Owns the USB detector's lifecycle and the
/// orchestrator's state events. The invoker supplied at construction marshals property changes to
/// the UI thread, so the UI binds directly and tests supply an immediate invoker. Log messages go
/// to the output supplied at construction, which marshals them (see the composition root).
/// </summary>
public partial class FlashSession : ObservableObject
{
    private const int MaxFirmwareHistory = 10;

    private readonly Func<Func<Task>, Task> _uiInvoker;
    private readonly IUsbEventsDetector _usbDetector;
    private readonly FlashOrchestrator _orchestrator;
    private readonly IFlashToolProvider _toolProvider;
    private readonly MessageSink _output;

    [ObservableProperty] private string _firmwarePath = "";
    [ObservableProperty] private string _selectedMcu = "";
    [ObservableProperty] private bool _autoFlashEnabled;
    [ObservableProperty] private bool _showAllDevices;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(FlashCommand))]
    private bool _canFlash;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ResetCommand))]
    private bool _canReset;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ClearEepromCommand))]
    [NotifyCanExecuteChangedFor(nameof(SetLeftHandCommand))]
    [NotifyCanExecuteChangedFor(nameof(SetRightHandCommand))]
    private bool _canClearEeprom;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ClearResourcesCommand))]
    private bool _canClearResources = true;

    public ObservableCollection<string> FirmwareHistory { get; } = [];
    public ObservableCollection<McuItem> McuList { get; } = [];


    public FlashSession(
        Func<Func<Task>, Task> uiInvoker,
        IUsbEventsDetector usbDetector,
        FlashOrchestrator orchestrator,
        IFlashToolProvider toolProvider,
        MessageSink output)
    {
        _uiInvoker = uiInvoker;
        _usbDetector = usbDetector;
        _orchestrator = orchestrator;
        _toolProvider = toolProvider;
        _output = output;

        _orchestrator.StateChanged += () => Invoke(UpdateCanExecute);

        _usbDetector.DeviceConnected += OnDeviceConnected;
        _usbDetector.DeviceDisconnected += OnDeviceDisconnected;
    }

    private void Invoke(Action action) => _ = _uiInvoker(() => { action(); return Task.CompletedTask; });

    public McuItem? SelectedMcuPair
    {
        get => McuList.FirstOrDefault(m => m.Key == SelectedMcu) ?? McuList.FirstOrDefault();
        set
        {
            if (value is not null)
                SelectedMcu = value.Key;
            OnPropertyChanged();
        }
    }

    partial void OnSelectedMcuChanged(string value) => OnPropertyChanged(nameof(SelectedMcuPair));

    /// <summary>Applies the flash-related settings and loads the MCU list.</summary>
    public void LoadFrom(AppSettings settings)
    {
        FirmwarePath = settings.FirmwareFilePath;
        SelectedMcu = settings.SelectedMcu;
        ShowAllDevices = settings.ShowAllDevices;
        AutoFlashEnabled = settings.AutoFlashEnabled;

        foreach (string item in settings.FirmwareFileHistory)
            FirmwareHistory.Add(item);

        LoadMcuList();
    }

    public void SaveTo(AppSettings settings)
    {
        settings.FirmwareFilePath = FirmwarePath;
        settings.FirmwareFileHistory = [.. FirmwareHistory];
        settings.SelectedMcu = SelectedMcu;
        settings.ShowAllDevices = ShowAllDevices;
        settings.AutoFlashEnabled = AutoFlashEnabled;
    }

    private void LoadMcuList()
    {
        try
        {
            using Stream? stream = typeof(FlashSession).Assembly
                .GetManifestResourceStream("QmkToolbox.Desktop.Resources.mcu-list.txt");
            if (stream == null)
                return;
            using var reader = new StreamReader(stream);
            string content = reader.ReadToEnd();
            foreach (string line in content.Split('\n'))
            {
                string[] parts = line.Trim().Split(':', 2);
                if (parts.Length == 2)
                    McuList.Add(new McuItem(parts[0], parts[1]));
            }
            if (string.IsNullOrEmpty(SelectedMcu) && McuList.Count > 0)
                SelectedMcu = McuList[0].Key;
        }
        catch (Exception ex)
        {
            Output($"Failed to load MCU list: {ex.Message}", MessageType.Error);
        }
    }

    /// <summary>
    /// Extracts flash-tool resources, then starts the USB detector, in that order so an auto-flash
    /// triggered by an early arrival can't hit missing tool binaries. Both calls block, so the
    /// sequence runs on a thread pool thread; failures go to the output.
    /// </summary>
    public void Start() => _ = StartAsync();

    internal Task StartAsync() => Task.Run(() =>
    {
        try
        { _toolProvider.EnsureResourceFolder(); }
        catch (Exception ex) { Output($"Failed to extract resources: {ex.Message}", MessageType.Error); }
        try
        { _usbDetector.Start(); }
        catch (Exception ex) { Output($"USB device enumeration failed: {ex.Message}", MessageType.Error); }
    });

    public void Stop()
    {
        _usbDetector.Stop();
        _usbDetector.Dispose();
    }

    internal Task? AutoFlashTask { get; private set; }

    private void OnDeviceConnected(UsbDeviceInfo device)
        => _ = _uiInvoker(async () =>
        {
            // Completes synchronously for VID/PID-mapped devices; unmapped mass-storage devices
            // resolve only after the volume probe, so auto-flash waits until the volume is
            // mounted.
            bool bootloaderAdded = await _orchestrator.OnDeviceConnectedAsync(device, ShowAllDevices);
            if (!bootloaderAdded || !AutoFlashEnabled)
                return;
            if (!ValidateFirmware("Auto-flash: "))
                return;
            AutoFlashTask = AutoFlashAsync();
            await AutoFlashTask;
        });

    private async Task AutoFlashAsync()
    {
        try
        {
            if (!await _orchestrator.FlashAllAsync(SelectedMcu, FirmwarePath))
                Output("Auto-flash: an operation is already in progress, skipping", MessageType.Info);
        }
        catch (Exception ex)
        {
            Output($"Auto-flash failed: {ex.Message}", MessageType.Error);
        }
    }

    private void OnDeviceDisconnected(UsbDeviceInfo device)
        => Invoke(() => _orchestrator.OnDeviceDisconnected(device, ShowAllDevices));

    // The orchestrator owns the in-flight invariant (FlashOrchestrator.IsBusy); every readiness
    // flag derives from it, so a USB connect/disconnect event mid-operation can't re-enable
    // the buttons.
    private void UpdateCanExecute()
    {
        bool busy = _orchestrator.IsBusy;
        bool flash = _orchestrator.HasBootloaders && !busy;
        bool reset = _orchestrator.HasResettable && !busy;
        bool eeprom = _orchestrator.HasEepromFlashable && !busy;
        if (flash != CanFlash || reset != CanReset)
        {
            Output($"[STATE] CanFlash:{CanFlash}->{flash}  CanReset:{CanReset}->{reset}" +
                $"  (bootloaders:{_orchestrator.BootloaderCount})", MessageType.Debug);
        }
        CanFlash = flash;
        CanReset = reset;
        CanClearEeprom = eeprom;
        CanClearResources = !busy;
    }

    private bool ValidateFirmware(string prefix)
    {
        if (string.IsNullOrEmpty(FirmwarePath))
        {
            Output($"{prefix}no firmware file selected", MessageType.Error);
            return false;
        }
        if (!File.Exists(FirmwarePath))
        {
            Output($"{prefix}firmware file does not exist", MessageType.Error);
            return false;
        }
        return true;
    }

    [RelayCommand(CanExecute = nameof(CanFlash))]
    public async Task FlashAsync()
    {
        if (ValidateFirmware(""))
            await _orchestrator.FlashAllAsync(SelectedMcu, FirmwarePath);
    }

    [RelayCommand(CanExecute = nameof(CanReset))]
    public Task ResetAsync() => _orchestrator.ResetAllAsync(SelectedMcu);

    [RelayCommand(CanExecute = nameof(CanClearEeprom))]
    public Task ClearEepromAsync() =>
        FlashEepromAsync("reset.eep", "Attempting to clear EEPROM, please don't remove device", "EEPROM clear complete");

    [RelayCommand(CanExecute = nameof(CanClearEeprom))]
    public Task SetLeftHandAsync() => SetHandednessAsync(left: true);

    [RelayCommand(CanExecute = nameof(CanClearEeprom))]
    public Task SetRightHandAsync() => SetHandednessAsync(left: false);

    private Task<bool> SetHandednessAsync(bool left) =>
        FlashEepromAsync(left ? "reset_left.eep" : "reset_right.eep",
            "Attempting to set handedness, please don't remove device", "EEPROM write complete");

    private Task<bool> FlashEepromAsync(string eepFile, string startMessage, string completeMessage) =>
        _orchestrator.FlashEepromAsync(SelectedMcu, _toolProvider.GetDataFilePath(eepFile), startMessage, completeMessage);

    // ClearAndReExtract blocks; Task.Run keeps it off the UI thread. The orchestrator's gate
    // serialises it with flashing, so it can't delete tool binaries mid-flash and is refused
    // while a flash runs.
    [RelayCommand(CanExecute = nameof(CanClearResources))]
    public Task ClearResourcesAsync() =>
        _orchestrator.RunExclusiveAsync(() => Task.Run(_toolProvider.ClearAndReExtract));

    public void SetFirmwarePath(string path)
    {
        if (string.IsNullOrEmpty(path))
            return;
        FirmwareHistory.Remove(path);
        FirmwareHistory.Insert(0, path);
        while (FirmwareHistory.Count > MaxFirmwareHistory)
            FirmwareHistory.RemoveAt(FirmwareHistory.Count - 1);
        FirmwarePath = path;
    }

    private void Output(string message, MessageType type) => _output(message, type);
}
