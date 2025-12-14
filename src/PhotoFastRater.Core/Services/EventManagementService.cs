using PhotoFastRater.Core.Database.Repositories;
using PhotoFastRater.Core.Models;

namespace PhotoFastRater.Core.Services;

public class EventManagementService
{
    private readonly EventRepository _eventRepository;
    private readonly PhotoRepository _photoRepository;

    public EventManagementService(EventRepository eventRepository, PhotoRepository photoRepository)
    {
        _eventRepository = eventRepository;
        _photoRepository = photoRepository;
    }

    // 自動グルーピング: 日付・場所で近い写真をグループ化
    public async Task<List<Event>> AutoGroupByProximityAsync(
        List<Photo> photos,
        TimeSpan maxTimeDifference,
        double maxDistanceKm = 5.0)
    {
        var groups = new List<List<Photo>>();
        var sorted = photos.OrderBy(p => p.DateTaken).ToList();

        if (sorted.Count == 0)
            return new List<Event>();

        var currentGroup = new List<Photo> { sorted[0] };

        for (int i = 1; i < sorted.Count; i++)
        {
            var prev = sorted[i - 1];
            var current = sorted[i];

            var timeDiff = current.DateTaken - prev.DateTaken;
            var distance = CalculateDistance(
                prev.Latitude, prev.Longitude,
                current.Latitude, current.Longitude);

            if (timeDiff <= maxTimeDifference &&
                (distance == null || distance <= maxDistanceKm))
            {
                currentGroup.Add(current);
            }
            else
            {
                groups.Add(currentGroup);
                currentGroup = new List<Photo> { current };
            }
        }
        groups.Add(currentGroup);

        // イベント作成
        var events = new List<Event>();
        foreach (var group in groups.Where(g => g.Count > 1))
        {
            var evt = new Event
            {
                Name = $"イベント {group.First().DateTaken:yyyy/MM/dd}",
                Type = EventType.Event,
                StartDate = group.Min(p => p.DateTaken),
                EndDate = group.Max(p => p.DateTaken),
                Location = group.First().LocationName,
                PhotoCount = group.Count
            };

            var created = await _eventRepository.AddAsync(evt);

            foreach (var photo in group)
            {
                await _eventRepository.AddPhotoToEventAsync(photo.Id, created.Id);
            }

            events.Add(created);
        }

        return events;
    }

    // 手動イベント作成
    public async Task<Event> CreateEventAsync(
        string name, List<int> photoIds, string? description = null)
    {
        var evt = new Event
        {
            Name = name,
            Description = description,
            Type = EventType.Custom,
            PhotoCount = photoIds.Count
        };

        var created = await _eventRepository.AddAsync(evt);

        foreach (var photoId in photoIds)
        {
            await _eventRepository.AddPhotoToEventAsync(photoId, created.Id);
        }

        return created;
    }

    private static double? CalculateDistance(
        double? lat1, double? lon1, double? lat2, double? lon2)
    {
        if (!lat1.HasValue || !lon1.HasValue ||
            !lat2.HasValue || !lon2.HasValue)
            return null;

        // Haversine formula
        const double R = 6371; // 地球の半径 (km)
        var dLat = ToRadians(lat2.Value - lat1.Value);
        var dLon = ToRadians(lon2.Value - lon1.Value);

        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(ToRadians(lat1.Value)) * Math.Cos(ToRadians(lat2.Value)) *
                Math.Sin(dLon / 2) * Math.Sin(dLon / 2);

        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return R * c;
    }

    private static double ToRadians(double degrees)
    {
        return degrees * Math.PI / 180.0;
    }
}
