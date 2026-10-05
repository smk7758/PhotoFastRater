using PhotoFastRater.Infrastructure.Database.Repositories;
using PhotoFastRater.Core.Models;

namespace PhotoFastRater.Infrastructure.Services;

public class EventManagementService
{
    private readonly EventRepository _eventRepository;
    private readonly PhotoRepository _photoRepository;

    public EventManagementService(EventRepository eventRepository, PhotoRepository photoRepository)
    {
        _eventRepository = eventRepository;
        _photoRepository = photoRepository;
    }

    /// <summary>Builds deterministic candidates without changing the database.</summary>
    public IReadOnlyList<EventCandidate> PreviewAutoGroups(
        IReadOnlyList<Photo> photos,
        TimeSpan maxTimeDifference,
        double maxDistanceKm = 5.0)
    {
        var groups = new List<List<Photo>>();
        var sorted = photos.OrderBy(p => p.DateTaken).ToList();

        if (sorted.Count == 0)
            return [];

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

        return groups.Where(group => group.Count > 1).Select(group => new EventCandidate(
            Key: CreateGroupKey(group),
            Name: $"イベント {group[0].DateTaken:yyyy/MM/dd}",
            StartDate: group.Min(photo => photo.DateTaken),
            EndDate: group.Max(photo => photo.DateTaken),
            Location: group[0].LocationName,
            PhotoIds: group.Select(photo => photo.Id).Order().ToArray())).ToArray();
    }

    /// <summary>Confirms previewed groups; stable keys make repeated confirmation idempotent.</summary>
    public async Task<int> ConfirmAutoGroupsAsync(
        IReadOnlyCollection<EventCandidate> candidates,
        CancellationToken cancellationToken = default)
    {
        var created = 0;
        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await _eventRepository.ConfirmCandidateAsync(candidate, cancellationToken))
                created++;
        }
        return created;
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

    private static string CreateGroupKey(IReadOnlyCollection<Photo> photos)
    {
        var ids = string.Join(',', photos.Select(photo => photo.Id).Order());
        return Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(ids)));
    }
}
