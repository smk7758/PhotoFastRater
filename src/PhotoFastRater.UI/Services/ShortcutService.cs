using System.Text.Json;
using System.Windows.Input;
using PhotoFastRater.UI.Models;

namespace PhotoFastRater.UI.Services;

public class ShortcutService
{
    private readonly string _filePath;

    /// <summary>Uses the existing installed location for callers without an explicit profile.</summary>
    public ShortcutService() : this(new ApplicationPaths()) { }

    /// <summary>Separates shortcut preferences from other profiles.</summary>
    public ShortcutService(ApplicationPaths paths) => _filePath = paths.Shortcuts;

    public static readonly IReadOnlyList<ShortcutEntry> Defaults = new List<ShortcutEntry>
    {
        new() { CommandName = "NavigateUp",     DisplayName = "上に移動",               Key = Key.Up },
        new() { CommandName = "NavigateDown",   DisplayName = "下に移動",               Key = Key.Down },
        new() { CommandName = "NavigateLeft",   DisplayName = "左に移動",               Key = Key.Left },
        new() { CommandName = "NavigateRight",  DisplayName = "右に移動",               Key = Key.Right },
        new() { CommandName = "OpenFolder",     DisplayName = "フォルダを開く",         Key = Key.O, Modifiers = ModifierKeys.Control },
        new() { CommandName = "SetRating0",     DisplayName = "レーティング 0",         Key = Key.D0 },
        new() { CommandName = "SetRating1",     DisplayName = "レーティング 1",         Key = Key.D1 },
        new() { CommandName = "SetRating2",     DisplayName = "レーティング 2",         Key = Key.D2 },
        new() { CommandName = "SetRating3",     DisplayName = "レーティング 3",         Key = Key.D3 },
        new() { CommandName = "SetRating4",     DisplayName = "レーティング 4",         Key = Key.D4 },
        new() { CommandName = "SetRating5",     DisplayName = "レーティング 5",         Key = Key.D5 },
        new() { CommandName = "ToggleFavorite", DisplayName = "お気に入りトグル",       Key = Key.F },
        new() { CommandName = "ToggleReject",   DisplayName = "リジェクトトグル",       Key = Key.X },
        new() { CommandName = "ReloadFolder",   DisplayName = "フォルダ再読み込み",     Key = Key.F5 },
        new() { CommandName = "NavigateHome",   DisplayName = "先頭に移動",             Key = Key.Home },
        new() { CommandName = "NavigateEnd",    DisplayName = "終端に移動",             Key = Key.End },
    };

    public List<ShortcutEntry> Load()
    {
        if (!File.Exists(_filePath))
            return Defaults.Select(Clone).ToList();

        try
        {
            var json = File.ReadAllText(_filePath);
            var dtos = JsonSerializer.Deserialize<List<ShortcutEntryDto>>(json);
            if (dtos == null) return Defaults.Select(Clone).ToList();

            return dtos.Select(dto => new ShortcutEntry
            {
                CommandName = dto.CommandName ?? string.Empty,
                DisplayName = dto.DisplayName ?? string.Empty,
                Key = Enum.TryParse<Key>(dto.Key, out var k) ? k : Key.None,
                Modifiers = Enum.TryParse<ModifierKeys>(dto.Modifiers, out var m) ? m : ModifierKeys.None
            }).ToList();
        }
        catch
        {
            return Defaults.Select(Clone).ToList();
        }
    }

    public void Save(List<ShortcutEntry> entries)
    {
        ValidateEntries(entries);
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
        var dtos = entries.Select(e => new ShortcutEntryDto
        {
            CommandName = e.CommandName,
            DisplayName = e.DisplayName,
            Key = e.Key.ToString(),
            Modifiers = e.Modifiers.ToString()
        }).ToList();
        AtomicPreferences.Write(_filePath, dtos);
    }

    /// <summary>Rejects ambiguous assignments and reserved focus keys instead of silently binding the first entry.</summary>
    public static void ValidateEntries(IReadOnlyList<ShortcutEntry> entries)
    {
        if (entries.Any(entry => entry.Key is Key.None or Key.Tab or Key.Escape))
            throw new ArgumentException("Tabはフォーカス移動、Escは取消に使います。別のキーを指定してください。");
        if (entries.GroupBy(entry => (entry.Key, entry.Modifiers)).Any(group => group.Count() > 1))
            throw new ArgumentException("同じキーの組み合わせが重複しています。");
    }

    private static ShortcutEntry Clone(ShortcutEntry e) => new()
    {
        CommandName = e.CommandName,
        DisplayName = e.DisplayName,
        Key = e.Key,
        Modifiers = e.Modifiers
    };

    private class ShortcutEntryDto
    {
        public string? CommandName { get; set; }
        public string? DisplayName { get; set; }
        public string? Key { get; set; }
        public string? Modifiers { get; set; }
    }
}
