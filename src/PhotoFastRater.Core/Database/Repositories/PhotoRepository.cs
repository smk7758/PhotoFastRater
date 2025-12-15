using Microsoft.EntityFrameworkCore;
using PhotoFastRater.Core.Models;

namespace PhotoFastRater.Core.Database.Repositories;

public class PhotoRepository
{
    private readonly PhotoDbContext _context;

    public PhotoRepository(PhotoDbContext context)
    {
        _context = context;
    }

    public async Task<Photo?> GetByIdAsync(int id)
    {
        return await _context.Photos
            .Include(p => p.Events)
            .ThenInclude(e => e.Event)
            .FirstOrDefaultAsync(p => p.Id == id);
    }

    public async Task<List<Photo>> GetAllAsync()
    {
        return await _context.Photos
            .OrderByDescending(p => p.DateTaken)
            .ToListAsync();
    }

    public async Task<List<Photo>> GetByDateRangeAsync(DateTime startDate, DateTime endDate)
    {
        return await _context.Photos
            .Where(p => p.DateTaken >= startDate && p.DateTaken <= endDate)
            .OrderByDescending(p => p.DateTaken)
            .ToListAsync();
    }

    public async Task<List<Photo>> GetByCameraAsync(string cameraModel)
    {
        return await _context.Photos
            .Where(p => p.CameraModel == cameraModel)
            .OrderByDescending(p => p.DateTaken)
            .ToListAsync();
    }

    public async Task<List<Photo>> GetByLensAsync(string lensModel)
    {
        return await _context.Photos
            .Where(p => p.LensModel == lensModel)
            .OrderByDescending(p => p.DateTaken)
            .ToListAsync();
    }

    public async Task<List<Photo>> GetByRatingAsync(int rating)
    {
        return await _context.Photos
            .Where(p => p.Rating == rating)
            .OrderByDescending(p => p.DateTaken)
            .ToListAsync();
    }

    public async Task<Photo> AddAsync(Photo photo)
    {
        _context.Photos.Add(photo);
        await _context.SaveChangesAsync();
        return photo;
    }

    public async Task UpdateAsync(Photo photo)
    {
        _context.Photos.Update(photo);
        await _context.SaveChangesAsync();
    }

    public async Task DeleteAsync(int id)
    {
        var photo = await _context.Photos.FindAsync(id);
        if (photo != null)
        {
            _context.Photos.Remove(photo);
            await _context.SaveChangesAsync();
        }
    }

    public async Task<bool> ExistsAsync(string filePath)
    {
        return await _context.Photos.AnyAsync(p => p.FilePath == filePath);
    }

    public async Task<Photo?> GetByFilePathAsync(string filePath)
    {
        return await _context.Photos
            .FirstOrDefaultAsync(p => p.FilePath == filePath);
    }
}
