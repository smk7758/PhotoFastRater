using Microsoft.EntityFrameworkCore;
using PhotoFastRater.Core.Models;

namespace PhotoFastRater.Core.Database.Repositories;

/// <summary>
/// 管理フォルダのリポジトリ
/// </summary>
public class ManagedFolderRepository
{
    private readonly PhotoDbContext _context;

    public ManagedFolderRepository(PhotoDbContext context)
    {
        _context = context;
    }

    /// <summary>
    /// フォルダを追加
    /// </summary>
    public async Task<ManagedFolder> AddAsync(ManagedFolder folder)
    {
        _context.ManagedFolders.Add(folder);
        await _context.SaveChangesAsync();
        return folder;
    }

    /// <summary>
    /// IDでフォルダを取得
    /// </summary>
    public async Task<ManagedFolder?> GetByIdAsync(int id)
    {
        return await _context.ManagedFolders.FindAsync(id);
    }

    /// <summary>
    /// パスでフォルダを取得
    /// </summary>
    public async Task<ManagedFolder?> GetByPathAsync(string folderPath)
    {
        return await _context.ManagedFolders
            .FirstOrDefaultAsync(f => f.FolderPath == folderPath);
    }

    /// <summary>
    /// すべてのフォルダを取得
    /// </summary>
    public async Task<List<ManagedFolder>> GetAllAsync()
    {
        return await _context.ManagedFolders
            .OrderBy(f => f.FolderPath)
            .ToListAsync();
    }

    /// <summary>
    /// 有効なフォルダのみを取得
    /// </summary>
    public async Task<List<ManagedFolder>> GetActiveAsync()
    {
        return await _context.ManagedFolders
            .Where(f => f.IsActive)
            .OrderBy(f => f.FolderPath)
            .ToListAsync();
    }

    /// <summary>
    /// フォルダを更新
    /// </summary>
    public async Task UpdateAsync(ManagedFolder folder)
    {
        _context.ManagedFolders.Update(folder);
        await _context.SaveChangesAsync();
    }

    /// <summary>
    /// フォルダを削除
    /// </summary>
    public async Task DeleteAsync(int id)
    {
        var folder = await GetByIdAsync(id);
        if (folder != null)
        {
            _context.ManagedFolders.Remove(folder);
            await _context.SaveChangesAsync();
        }
    }

    /// <summary>
    /// フォルダが既に存在するかチェック
    /// </summary>
    public async Task<bool> ExistsAsync(string folderPath)
    {
        return await _context.ManagedFolders
            .AnyAsync(f => f.FolderPath == folderPath);
    }

    /// <summary>
    /// フォルダの写真数を更新
    /// </summary>
    public async Task UpdatePhotoCountAsync(int folderId, int photoCount)
    {
        var folder = await GetByIdAsync(folderId);
        if (folder != null)
        {
            folder.PhotoCount = photoCount;
            folder.LastScanDate = DateTime.Now;
            await UpdateAsync(folder);
        }
    }
}
