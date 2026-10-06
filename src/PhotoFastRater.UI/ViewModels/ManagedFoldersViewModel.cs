using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PhotoFastRater.Core.Abstractions;
using PhotoFastRater.Core.Models;
using PhotoFastRater.Infrastructure.Services;
using PhotoFastRater.Infrastructure.Database.Repositories;

namespace PhotoFastRater.UI.ViewModels;

/// <summary>
/// 管理フォルダViewModel
/// </summary>
public partial class ManagedFoldersViewModel : ViewModelBase
{
    public static IReadOnlyList<PatternType> PatternTypes { get; } = [PatternType.Wildcard, PatternType.Regex, PatternType.Exact];

    /// <summary>Persists explicitly edited rows, keeping unsaved changes visible until the user chooses to save.</summary>
    [RelayCommand]
    private async Task SaveEditsAsync()
    {
        try
        {
            foreach (var item in ExclusionPatterns)
            {
                if (string.IsNullOrWhiteSpace(item.PatternString))
                    throw new ArgumentException("除外パターンは空にできません。");
                if (item.Type == PatternType.Regex)
                    _ = new System.Text.RegularExpressions.Regex(item.PatternString, System.Text.RegularExpressions.RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(250));
            }
            foreach (var item in Folders)
            {
                item.UpdateModel();
                await _folderRepository.UpdateAsync(item.GetModel());
            }
            foreach (var item in ExclusionPatterns)
            {
                item.UpdateModel();
                await _patternRepository.UpdateAsync(item.GetModel());
            }
            ScanStatus = "管理フォルダと除外パターンを保存しました。";
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException or Microsoft.EntityFrameworkCore.DbUpdateException)
        {
            ScanStatus = "保存が完了していません。入力とアクセス権を確認してください。";
            await _interaction.NotifyAsync("管理設定の保存エラー", exception.Message, UserNotificationKind.Error);
        }
    }
    private readonly ManagedFolderService _folderService;
    private readonly ManagedFolderRepository _folderRepository;
    private readonly FolderExclusionPatternRepository _patternRepository;
    private readonly IUserInteractionService _interaction;

    [ObservableProperty]
    private ObservableCollection<ManagedFolderItemViewModel> _folders = new();

    [ObservableProperty]
    private ObservableCollection<ExclusionPatternViewModel> _exclusionPatterns = new();

    [ObservableProperty]
    private ManagedFolderItemViewModel? _selectedFolder;

    [ObservableProperty]
    private ExclusionPatternViewModel? _selectedPattern;

    [ObservableProperty]
    private bool _isScanning = false;

    [ObservableProperty]
    private string _scanStatus = string.Empty;

    public ManagedFoldersViewModel(
        ManagedFolderService folderService,
        FolderExclusionPatternRepository patternRepository,
        ManagedFolderRepository folderRepository,
        IUserInteractionService interaction)
    {
        _folderService = folderService;
        _patternRepository = patternRepository;
        _folderRepository = folderRepository;
        _interaction = interaction;
    }

    /// <summary>
    /// データの読み込み
    /// </summary>
    public async Task LoadAsync()
    {
        await LoadFoldersAsync();
        await LoadPatternsAsync();
    }

    private async Task LoadFoldersAsync()
    {
        var folders = await _folderService.GetAllFoldersAsync();
        Folders.Clear();
        foreach (var folder in folders)
        {
            Folders.Add(new ManagedFolderItemViewModel(folder));
        }
    }

    private async Task LoadPatternsAsync()
    {
        var patterns = await _patternRepository.GetAllAsync();
        ExclusionPatterns.Clear();
        foreach (var pattern in patterns)
        {
            ExclusionPatterns.Add(new ExclusionPatternViewModel(pattern));
        }
    }

    /// <summary>
    /// フォルダを追加
    /// </summary>
    [RelayCommand]
    private async Task AddFolderAsync()
    {
        var selectedPath = await _interaction.SelectFolderAsync("管理するフォルダを選択してください");
        if (!string.IsNullOrWhiteSpace(selectedPath))
        {
            try
            {
                var folder = await _folderService.AddFolderAsync(selectedPath, isRecursive: true);
                Folders.Add(new ManagedFolderItemViewModel(folder));
                await _interaction.NotifyAsync("成功", $"フォルダを追加しました: {selectedPath}", UserNotificationKind.Information);
            }
            catch (Exception ex)
            {
                await _interaction.NotifyAsync("エラー", $"エラー: {ex.Message}", UserNotificationKind.Error);
            }
        }
    }

    /// <summary>
    /// フォルダを削除
    /// </summary>
    [RelayCommand]
    private async Task RemoveFolderAsync()
    {
        if (SelectedFolder == null) return;

        var confirmed = await _interaction.ConfirmAsync(
            "確認",
            $"フォルダを削除しますか?\n{SelectedFolder.FolderPath}",
            CancellationToken.None);

        if (confirmed)
        {
            await _folderService.RemoveFolderAsync(SelectedFolder.Id);
            Folders.Remove(SelectedFolder);
        }
    }

    /// <summary>
    /// フォルダをスキャン
    /// </summary>
    [RelayCommand]
    private async Task ScanFolderAsync()
    {
        if (SelectedFolder == null) return;

        IsScanning = true;
        ScanStatus = "スキャン中...";

        try
        {
            var progress = new Progress<ScanProgress>(p =>
            {
                ScanStatus = p.Status;
            });

            var result = await _folderService.ScanFolderAsync(SelectedFolder.Id, progress);

            await LoadFoldersAsync();

            await _interaction.NotifyAsync(
                "スキャン完了",
                $"スキャン完了\n" +
                $"合計: {result.TotalFiles}ファイル\n" +
                $"新規: {result.NewFiles}ファイル\n" +
                $"既存: {result.ExistingFiles}ファイル\n" +
                $"除外: {result.ExcludedFiles}ファイル",
                UserNotificationKind.Information);
        }
        catch (Exception ex)
        {
            await _interaction.NotifyAsync("エラー", $"エラー: {ex.Message}", UserNotificationKind.Error);
        }
        finally
        {
            IsScanning = false;
            ScanStatus = string.Empty;
        }
    }

    /// <summary>
    /// すべてのフォルダをスキャン
    /// </summary>
    [RelayCommand]
    private async Task ScanAllFoldersAsync()
    {
        IsScanning = true;
        ScanStatus = "一括スキャン中...";

        try
        {
            var progress = new Progress<ScanProgress>(p =>
            {
                ScanStatus = p.Status;
            });

            var results = await _folderService.ScanAllActiveFoldersAsync(progress);

            await LoadFoldersAsync();

            var totalNew = results.Values.Sum(r => r.NewFiles);
            var totalExisting = results.Values.Sum(r => r.ExistingFiles);

            await _interaction.NotifyAsync(
                "一括スキャン完了",
                $"一括スキャン完了\n" +
                $"スキャンフォルダ数: {results.Count}\n" +
                $"新規: {totalNew}ファイル\n" +
                $"既存: {totalExisting}ファイル",
                UserNotificationKind.Information);
        }
        catch (Exception ex)
        {
            await _interaction.NotifyAsync("エラー", $"エラー: {ex.Message}", UserNotificationKind.Error);
        }
        finally
        {
            IsScanning = false;
            ScanStatus = string.Empty;
        }
    }

    /// <summary>
    /// 除外パターンを追加
    /// </summary>
    [RelayCommand]
    private async Task AddPatternAsync()
    {
        // 簡易的な入力ダイアログ（後でカスタムダイアログに置き換え可能）
        var pattern = await _interaction.PromptTextAsync(
            "除外パターン追加",
            "除外パターンを入力してください\n例: */temp/*, */backup/*");

        if (string.IsNullOrWhiteSpace(pattern)) return;

        try
        {
            var newPattern = new FolderExclusionPattern
            {
                Pattern = pattern,
                Type = PatternType.Wildcard,
                IsEnabled = true,
                CreatedDate = DateTime.Now
            };

            var added = await _patternRepository.AddAsync(newPattern);
            ExclusionPatterns.Add(new ExclusionPatternViewModel(added));

            await _interaction.NotifyAsync("成功", "除外パターンを追加しました", UserNotificationKind.Information);
        }
        catch (Exception ex)
        {
            await _interaction.NotifyAsync("エラー", $"エラー: {ex.Message}", UserNotificationKind.Error);
        }
    }

    /// <summary>
    /// 除外パターンを削除
    /// </summary>
    [RelayCommand]
    private async Task RemovePatternAsync()
    {
        if (SelectedPattern == null) return;

        var confirmed = await _interaction.ConfirmAsync(
            "確認",
            $"除外パターンを削除しますか?\n{SelectedPattern.PatternString}",
            CancellationToken.None);

        if (confirmed)
        {
            await _patternRepository.DeleteAsync(SelectedPattern.Id);
            ExclusionPatterns.Remove(SelectedPattern);
        }
    }

    /// <summary>
    /// フォルダの有効/無効を切り替え
    /// </summary>
    [RelayCommand]
    private async Task ToggleFolderActiveAsync(ManagedFolderItemViewModel folder)
    {
        await _folderService.ToggleFolderActiveAsync(folder.Id);
        await LoadFoldersAsync();
    }
}
