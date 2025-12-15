using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using PhotoFastRater.Core.Models;
using PhotoFastRater.Core.Services;
using PhotoFastRater.Core.Database.Repositories;
using MessageBox = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxImage = System.Windows.MessageBoxImage;
using MessageBoxResult = System.Windows.MessageBoxResult;

namespace PhotoFastRater.UI.ViewModels;

/// <summary>
/// 管理フォルダViewModel
/// </summary>
public partial class ManagedFoldersViewModel : ViewModelBase
{
    private readonly ManagedFolderService _folderService;
    private readonly FolderExclusionPatternRepository _patternRepository;

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
        FolderExclusionPatternRepository patternRepository)
    {
        _folderService = folderService;
        _patternRepository = patternRepository;
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
        var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "管理するフォルダを選択してください",
            ShowNewFolderButton = false
        };

        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            try
            {
                var folder = await _folderService.AddFolderAsync(dialog.SelectedPath, isRecursive: true);
                Folders.Add(new ManagedFolderItemViewModel(folder));
                MessageBox.Show($"フォルダを追加しました: {dialog.SelectedPath}", "成功",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"エラー: {ex.Message}", "エラー",
                    MessageBoxButton.OK, MessageBoxImage.Error);
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

        var result = MessageBox.Show(
            $"フォルダを削除しますか?\n{SelectedFolder.FolderPath}",
            "確認",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
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

            MessageBox.Show(
                $"スキャン完了\n" +
                $"合計: {result.TotalFiles}ファイル\n" +
                $"新規: {result.NewFiles}ファイル\n" +
                $"既存: {result.ExistingFiles}ファイル\n" +
                $"除外: {result.ExcludedFiles}ファイル",
                "スキャン完了",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"エラー: {ex.Message}", "エラー",
                MessageBoxButton.OK, MessageBoxImage.Error);
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

            MessageBox.Show(
                $"一括スキャン完了\n" +
                $"スキャンフォルダ数: {results.Count}\n" +
                $"新規: {totalNew}ファイル\n" +
                $"既存: {totalExisting}ファイル",
                "一括スキャン完了",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"エラー: {ex.Message}", "エラー",
                MessageBoxButton.OK, MessageBoxImage.Error);
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
        var pattern = Microsoft.VisualBasic.Interaction.InputBox(
            "除外パターンを入力してください\n例: */temp/*, */backup/*",
            "除外パターン追加",
            "");

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

            MessageBox.Show("除外パターンを追加しました", "成功",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"エラー: {ex.Message}", "エラー",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// 除外パターンを削除
    /// </summary>
    [RelayCommand]
    private async Task RemovePatternAsync()
    {
        if (SelectedPattern == null) return;

        var result = MessageBox.Show(
            $"除外パターンを削除しますか?\n{SelectedPattern.PatternString}",
            "確認",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
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
