using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using QmkToolbox.Desktop.Models;
using QmkToolbox.Desktop.ViewModels;

namespace QmkToolbox.Desktop.Views;

/// <summary>
/// Main application window: hosts firmware selection, flashing controls, and the log panel.
/// Lifecycle wiring lives in <see cref="MainWindowHost"/>; this class handles only drag-drop.
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        AddHandler(DragDrop.DropEvent, OnDrop);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        if (!e.DataTransfer.Contains(DataFormat.File))
        {
            e.DragEffects = DragDropEffects.None;
            return;
        }
        string? path = e.DataTransfer.TryGetFile()?.TryGetLocalPath();
        e.DragEffects = path != null && FirmwareFiles.IsFirmwareFile(path)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm)
            return;
        if (!e.DataTransfer.Contains(DataFormat.File))
            return;

        IStorageItem? file = e.DataTransfer.TryGetFile();
        if (file == null)
            return;

        string? path = file.TryGetLocalPath();
        if (path != null && FirmwareFiles.IsFirmwareFile(path))
            vm.Session.SetFirmwarePath(path);
    }
}
