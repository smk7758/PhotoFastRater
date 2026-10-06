using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;

namespace PhotoFastRater.UI.Models;

/// <summary>Notifies the editing grid immediately when a captured key changes.</summary>
public partial class ShortcutEntry : ObservableObject
{
    public string CommandName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(KeyDisplay))]
    private Key _key;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(KeyDisplay))]
    private ModifierKeys _modifiers;

    public string KeyDisplay => Modifiers == ModifierKeys.None
        ? Key.ToString()
        : $"{Modifiers}+{Key}";
}
