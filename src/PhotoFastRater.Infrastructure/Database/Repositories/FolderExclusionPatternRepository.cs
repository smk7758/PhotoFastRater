using Microsoft.EntityFrameworkCore;
using PhotoFastRater.Core.Models;

namespace PhotoFastRater.Infrastructure.Database.Repositories;

/// <summary>
/// フォルダ除外パターンのリポジトリ
/// </summary>
public class FolderExclusionPatternRepository
{
    private readonly IDbContextFactory<PhotoDbContext> _contextFactory;

    public FolderExclusionPatternRepository(IDbContextFactory<PhotoDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    /// <summary>
    /// パターンを追加
    /// </summary>
    public async Task<FolderExclusionPattern> AddAsync(FolderExclusionPattern pattern)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        context.FolderExclusionPatterns.Add(pattern);
        await context.SaveChangesAsync();
        return pattern;
    }

    /// <summary>
    /// IDでパターンを取得
    /// </summary>
    public async Task<FolderExclusionPattern?> GetByIdAsync(int id)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.FolderExclusionPatterns.FindAsync(id);
    }

    /// <summary>
    /// すべてのパターンを取得
    /// </summary>
    public async Task<List<FolderExclusionPattern>> GetAllAsync()
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.FolderExclusionPatterns
            .AsNoTracking()
            .OrderBy(p => p.Pattern)
            .ToListAsync();
    }

    /// <summary>
    /// 有効なパターンのみを取得
    /// </summary>
    public async Task<List<FolderExclusionPattern>> GetEnabledAsync()
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.FolderExclusionPatterns
            .AsNoTracking()
            .Where(p => p.IsEnabled)
            .OrderBy(p => p.Pattern)
            .ToListAsync();
    }

    /// <summary>
    /// パターンを更新
    /// </summary>
    public async Task UpdateAsync(FolderExclusionPattern pattern)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        context.FolderExclusionPatterns.Update(pattern);
        await context.SaveChangesAsync();
    }

    /// <summary>
    /// パターンを削除
    /// </summary>
    public async Task DeleteAsync(int id)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var pattern = await context.FolderExclusionPatterns.FindAsync(id);
        if (pattern != null)
        {
            context.FolderExclusionPatterns.Remove(pattern);
            await context.SaveChangesAsync();
        }
    }

    /// <summary>
    /// パターンが既に存在するかチェック
    /// </summary>
    public async Task<bool> ExistsAsync(string pattern)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.FolderExclusionPatterns
            .AnyAsync(p => p.Pattern == pattern);
    }
}
