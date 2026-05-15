using System.Windows.Input;

namespace PhotoFastRater.UI.Models;

public class ShortcutEntry
{
    public string CommandName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public Key Key { get; set; }
    public ModifierKeys Modifiers { get; set; } = ModifierKeys.None;

    public string KeyDisplay => Modifiers == ModifierKeys.None
        ? Key.ToString()
        : $"{Modifiers}+{Key}";
}
