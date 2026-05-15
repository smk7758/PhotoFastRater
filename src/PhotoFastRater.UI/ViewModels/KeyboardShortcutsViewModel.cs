using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PhotoFastRater.UI.Models;
using PhotoFastRater.UI.Services;

namespace PhotoFastRater.UI.ViewModels;

public partial class KeyboardShortcutsViewModel : ViewModelBase
{
    private readonly ShortcutService _service;

    public ObservableCollection<ShortcutEntry> Shortcuts { get; } = new();

    [ObservableProperty]
    private ShortcutEntry? _editingEntry;

    [ObservableProperty]
    private bool _isCapturing;

    [ObservableProperty]
    private string _pressedKeyText = string.Empty;

    public KeyboardShortcutsViewModel(ShortcutService service)
    {
        _service = service;
        foreach (var entry in service.Load())
            Shortcuts.Add(entry);
    }

    [RelayCommand]
    private void StartCapture(ShortcutEntry? entry)
    {
        EditingEntry = entry;
        IsCapturing = entry != null;
        PressedKeyText = IsCapturing ? "キーを押してください..." : string.Empty;
    }

    public void CaptureKey(Key key, ModifierKeys modifiers)
    {
        if (!IsCapturing || EditingEntry == null) return;

        if (key is Key.Escape)
        {
            IsCapturing = false;
            PressedKeyText = string.Empty;
            EditingEntry = null;
            return;
        }

        EditingEntry.Key = key;
        EditingEntry.Modifiers = modifiers;
        OnPropertyChanged(nameof(Shortcuts));
        IsCapturing = false;
        PressedKeyText = string.Empty;
        EditingEntry = null;
    }

    [RelayCommand]
    private void ResetToDefault()
    {
        Shortcuts.Clear();
        foreach (var entry in ShortcutService.Defaults)
            Shortcuts.Add(new ShortcutEntry
            {
                CommandName = entry.CommandName,
                DisplayName = entry.DisplayName,
                Key = entry.Key,
                Modifiers = entry.Modifiers
            });
    }

    public void Save()
    {
        _service.Save(Shortcuts.ToList());
    }
}
