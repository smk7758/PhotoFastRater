using Microsoft.EntityFrameworkCore;
using PhotoFastRater.Core.Models;

namespace PhotoFastRater.Infrastructure.Database.Repositories;

public class EventRepository
{
    private readonly IDbContextFactory<PhotoDbContext> _contextFactory;

    public EventRepository(IDbContextFactory<PhotoDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<Event?> GetByIdAsync(int id)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.Events
            .AsNoTracking()
            .Include(e => e.Photos)
            .ThenInclude(p => p.Photo)
            .FirstOrDefaultAsync(e => e.Id == id);
    }

    public async Task<List<Event>> GetAllAsync()
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.Events
            .AsNoTracking()
            .OrderByDescending(e => e.StartDate)
            .ToListAsync();
    }

    public async Task<Event> AddAsync(Event evt)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        context.Events.Add(evt);
        await context.SaveChangesAsync();
        return evt;
    }

    public async Task UpdateAsync(Event evt)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        context.Events.Update(evt);
        await context.SaveChangesAsync();
    }

    public async Task DeleteAsync(int id)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var evt = await context.Events.FindAsync(id);
        if (evt != null)
        {
            context.Events.Remove(evt);
            await context.SaveChangesAsync();
        }
    }

    public async Task AddPhotoToEventAsync(int photoId, int eventId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var mapping = new PhotoEventMapping
        {
            PhotoId = photoId,
            EventId = eventId,
            AddedDate = DateTime.Now
        };

        context.PhotoEventMappings.Add(mapping);
        await context.SaveChangesAsync();

        // イベントの写真数を更新
        var evt = await context.Events.FindAsync(eventId);
        if (evt != null)
        {
            evt.PhotoCount = await context.PhotoEventMappings
                .CountAsync(m => m.EventId == eventId);
            await context.SaveChangesAsync();
        }
    }

    public async Task RemovePhotoFromEventAsync(int photoId, int eventId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var mapping = await context.PhotoEventMappings
            .FirstOrDefaultAsync(m => m.PhotoId == photoId && m.EventId == eventId);

        if (mapping != null)
        {
            context.PhotoEventMappings.Remove(mapping);
            await context.SaveChangesAsync();

            // イベントの写真数を更新
            var evt = await context.Events.FindAsync(eventId);
            if (evt != null)
            {
                evt.PhotoCount = await context.PhotoEventMappings
                    .CountAsync(m => m.EventId == eventId);
                await context.SaveChangesAsync();
            }
        }
    }
}
