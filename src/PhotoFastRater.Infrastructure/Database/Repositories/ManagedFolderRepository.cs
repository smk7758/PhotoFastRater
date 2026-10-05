using Microsoft.EntityFrameworkCore;
using PhotoFastRater.Core.Models;

namespace PhotoFastRater.Infrastructure.Database.Repositories;

/// <summary>
/// 管理フォルダのリポジトリ
/// </summary>
public class ManagedFolderRepository
{
    private readonly IDbContextFactory<PhotoDbContext> _contextFactory;

    public ManagedFolderRepository(IDbContextFactory<PhotoDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    /// <summary>
    /// フォルダを追加
    /// </summary>
    public async Task<ManagedFolder> AddAsync(ManagedFolder folder)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        context.ManagedFolders.Add(folder);
        await context.SaveChangesAsync();
        return folder;
    }

    /// <summary>
    /// IDでフォルダを取得
    /// </summary>
    public async Task<ManagedFolder?> GetByIdAsync(int id)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.ManagedFolders.FindAsync(id);
    }

    /// <summary>
    /// パスでフォルダを取得
    /// </summary>
    public async Task<ManagedFolder?> GetByPathAsync(string folderPath)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.ManagedFolders
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.FolderPath == folderPath);
    }

    /// <summary>
    /// すべてのフォルダを取得
    /// </summary>
    public async Task<List<ManagedFolder>> GetAllAsync()
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.ManagedFolders
            .AsNoTracking()
            .OrderBy(f => f.FolderPath)
            .ToListAsync();
    }

    /// <summary>
    /// 有効なフォルダのみを取得
    /// </summary>
    public async Task<List<ManagedFolder>> GetActiveAsync()
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.ManagedFolders
            .AsNoTracking()
            .Where(f => f.IsActive)
            .OrderBy(f => f.FolderPath)
            .ToListAsync();
    }

    /// <summary>
    /// フォルダを更新
    /// </summary>
    public async Task UpdateAsync(ManagedFolder folder)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        context.ManagedFolders.Update(folder);
        await context.SaveChangesAsync();
    }

    /// <summary>
    /// フォルダを削除
    /// </summary>
    public async Task DeleteAsync(int id)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var folder = await context.ManagedFolders.FindAsync(id);
        if (folder != null)
        {
            context.ManagedFolders.Remove(folder);
            await context.SaveChangesAsync();
        }
    }

    /// <summary>
    /// フォルダが既に存在するかチェック
    /// </summary>
    public async Task<bool> ExistsAsync(string folderPath)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.ManagedFolders
            .AnyAsync(f => f.FolderPath == folderPath);
    }

    /// <summary>
    /// フォルダの写真数を更新
    /// </summary>
    public async Task UpdatePhotoCountAsync(int folderId, int photoCount)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var folder = await context.ManagedFolders.FindAsync(folderId);
        if (folder != null)
        {
            folder.PhotoCount = photoCount;
            folder.LastScanDate = DateTime.Now;
            await context.SaveChangesAsync();
        }
    }
}
