using Microsoft.EntityFrameworkCore;
using PhotoFastRater.Core.Models;

namespace PhotoFastRater.Core.Database.Repositories;

public class EventRepository
{
    private readonly PhotoDbContext _context;

    public EventRepository(PhotoDbContext context)
    {
        _context = context;
    }

    public async Task<Event?> GetByIdAsync(int id)
    {
        return await _context.Events
            .Include(e => e.Photos)
            .ThenInclude(p => p.Photo)
            .FirstOrDefaultAsync(e => e.Id == id);
    }

    public async Task<List<Event>> GetAllAsync()
    {
        return await _context.Events
            .OrderByDescending(e => e.StartDate)
            .ToListAsync();
    }

    public async Task<Event> AddAsync(Event evt)
    {
        _context.Events.Add(evt);
        await _context.SaveChangesAsync();
        return evt;
    }

    public async Task UpdateAsync(Event evt)
    {
        _context.Events.Update(evt);
        await _context.SaveChangesAsync();
    }

    public async Task DeleteAsync(int id)
    {
        var evt = await _context.Events.FindAsync(id);
        if (evt != null)
        {
            _context.Events.Remove(evt);
            await _context.SaveChangesAsync();
        }
    }

    public async Task AddPhotoToEventAsync(int photoId, int eventId)
    {
        var mapping = new PhotoEventMapping
        {
            PhotoId = photoId,
            EventId = eventId,
            AddedDate = DateTime.Now
        };

        _context.PhotoEventMappings.Add(mapping);
        await _context.SaveChangesAsync();

        // イベントの写真数を更新
        var evt = await _context.Events.FindAsync(eventId);
        if (evt != null)
        {
            evt.PhotoCount = await _context.PhotoEventMappings
                .CountAsync(m => m.EventId == eventId);
            await _context.SaveChangesAsync();
        }
    }

    public async Task RemovePhotoFromEventAsync(int photoId, int eventId)
    {
        var mapping = await _context.PhotoEventMappings
            .FirstOrDefaultAsync(m => m.PhotoId == photoId && m.EventId == eventId);

        if (mapping != null)
        {
            _context.PhotoEventMappings.Remove(mapping);
            await _context.SaveChangesAsync();

            // イベントの写真数を更新
            var evt = await _context.Events.FindAsync(eventId);
            if (evt != null)
            {
                evt.PhotoCount = await _context.PhotoEventMappings
                    .CountAsync(m => m.EventId == eventId);
                await _context.SaveChangesAsync();
            }
        }
    }
}
