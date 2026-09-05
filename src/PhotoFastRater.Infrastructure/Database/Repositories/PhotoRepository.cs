using Microsoft.EntityFrameworkCore;
using PhotoFastRater.Core.Models;

namespace PhotoFastRater.Infrastructure.Database.Repositories;

public class PhotoRepository
{
    private readonly IDbContextFactory<PhotoDbContext> _contextFactory;

    public PhotoRepository(IDbContextFactory<PhotoDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<Photo?> GetByIdAsync(int id)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.Photos
            .AsNoTracking()
            .Include(p => p.Events)
            .ThenInclude(e => e.Event)
            .FirstOrDefaultAsync(p => p.Id == id);
    }

    public async Task<List<Photo>> GetAllAsync()
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.Photos
            .AsNoTracking()
            .OrderByDescending(p => p.DateTaken)
            .ToListAsync();
    }

    public async Task<List<Photo>> GetByDateRangeAsync(DateTime startDate, DateTime endDate)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.Photos
            .AsNoTracking()
            .Where(p => p.DateTaken >= startDate && p.DateTaken <= endDate)
            .OrderByDescending(p => p.DateTaken)
            .ToListAsync();
    }

    public async Task<List<Photo>> GetByCameraAsync(string cameraModel)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.Photos
            .AsNoTracking()
            .Where(p => p.CameraModel == cameraModel)
            .OrderByDescending(p => p.DateTaken)
            .ToListAsync();
    }

    public async Task<List<Photo>> GetByLensAsync(string lensModel)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.Photos
            .AsNoTracking()
            .Where(p => p.LensModel == lensModel)
            .OrderByDescending(p => p.DateTaken)
            .ToListAsync();
    }

    public async Task<List<Photo>> GetByRatingAsync(int rating)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.Photos
            .AsNoTracking()
            .Where(p => p.Rating == rating)
            .OrderByDescending(p => p.DateTaken)
            .ToListAsync();
    }

    public async Task<Photo> AddAsync(Photo photo)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        context.Photos.Add(photo);
        await context.SaveChangesAsync();
        return photo;
    }

    public async Task UpdateAsync(Photo photo)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        context.Photos.Update(photo);
        await context.SaveChangesAsync();
    }

    public async Task DeleteAsync(int id)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var photo = await context.Photos.FindAsync(id);
        if (photo != null)
        {
            context.Photos.Remove(photo);
            await context.SaveChangesAsync();
        }
    }

    public async Task<bool> ExistsAsync(string filePath)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.Photos.AnyAsync(p => p.FilePath == filePath);
    }

    public async Task<Photo?> GetByFilePathAsync(string filePath)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.Photos
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.FilePath == filePath);
    }
}
