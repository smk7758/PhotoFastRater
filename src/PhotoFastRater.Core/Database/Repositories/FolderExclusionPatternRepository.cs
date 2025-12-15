using Microsoft.EntityFrameworkCore;
using PhotoFastRater.Core.Models;

namespace PhotoFastRater.Core.Database.Repositories;

/// <summary>
/// フォルダ除外パターンのリポジトリ
/// </summary>
public class FolderExclusionPatternRepository
{
    private readonly PhotoDbContext _context;

    public FolderExclusionPatternRepository(PhotoDbContext context)
    {
        _context = context;
    }

    /// <summary>
    /// パターンを追加
    /// </summary>
    public async Task<FolderExclusionPattern> AddAsync(FolderExclusionPattern pattern)
    {
        _context.FolderExclusionPatterns.Add(pattern);
        await _context.SaveChangesAsync();
        return pattern;
    }

    /// <summary>
    /// IDでパターンを取得
    /// </summary>
    public async Task<FolderExclusionPattern?> GetByIdAsync(int id)
    {
        return await _context.FolderExclusionPatterns.FindAsync(id);
    }

    /// <summary>
    /// すべてのパターンを取得
    /// </summary>
    public async Task<List<FolderExclusionPattern>> GetAllAsync()
    {
        return await _context.FolderExclusionPatterns
            .OrderBy(p => p.Pattern)
            .ToListAsync();
    }

    /// <summary>
    /// 有効なパターンのみを取得
    /// </summary>
    public async Task<List<FolderExclusionPattern>> GetEnabledAsync()
    {
        return await _context.FolderExclusionPatterns
            .Where(p => p.IsEnabled)
            .OrderBy(p => p.Pattern)
            .ToListAsync();
    }

    /// <summary>
    /// パターンを更新
    /// </summary>
    public async Task UpdateAsync(FolderExclusionPattern pattern)
    {
        _context.FolderExclusionPatterns.Update(pattern);
        await _context.SaveChangesAsync();
    }

    /// <summary>
    /// パターンを削除
    /// </summary>
    public async Task DeleteAsync(int id)
    {
        var pattern = await GetByIdAsync(id);
        if (pattern != null)
        {
            _context.FolderExclusionPatterns.Remove(pattern);
            await _context.SaveChangesAsync();
        }
    }

    /// <summary>
    /// パターンが既に存在するかチェック
    /// </summary>
    public async Task<bool> ExistsAsync(string pattern)
    {
        return await _context.FolderExclusionPatterns
            .AnyAsync(p => p.Pattern == pattern);
    }
}
