using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PhotoFastRater.Infrastructure.Database.Repositories;
using PhotoFastRater.Core.Models;
using PhotoFastRater.Infrastructure.Services;

namespace PhotoFastRater.UI.ViewModels;

public partial class EventViewModel : ViewModelBase
{
    private readonly EventRepository _eventRepository;
    private readonly EventManagementService _eventService;
    private readonly PhotoRepository _photoRepository;

    [ObservableProperty]
    private ObservableCollection<Event> _events = new();

    [ObservableProperty]
    private Event? _selectedEvent;

    [ObservableProperty]
    private string _newEventName = string.Empty;

    public EventViewModel(EventRepository eventRepository, EventManagementService eventService, PhotoRepository photoRepository)
    {
        _eventRepository = eventRepository;
        _eventService = eventService;
        _photoRepository = photoRepository;
    }

    public async Task LoadEventsAsync()
    {
        var events = await _eventRepository.GetAllAsync();
        Events.Clear();
        foreach (var evt in events)
        {
            Events.Add(evt);
        }
    }

    [RelayCommand]
    public async Task CreateEventAsync(IReadOnlyCollection<int> photoIds)
    {
        if (string.IsNullOrWhiteSpace(NewEventName))
            return;
        if (photoIds.Count == 0)
            return;

        await _eventService.CreateEventAsync(NewEventName, photoIds.ToList());
        await LoadEventsAsync();
        NewEventName = string.Empty;
    }

    [RelayCommand]
    private async Task DeleteEventAsync(int eventId)
    {
        await _eventRepository.DeleteAsync(eventId);
        await LoadEventsAsync();
    }

    [RelayCommand]
    private async Task AutoGroupPhotosAsync()
    {
        // すべての写真を取得して自動グルーピング
        var photos = await _photoRepository.GetAllAsync();
        await _eventService.AutoGroupByProximityAsync(photos, TimeSpan.FromHours(2));
        await LoadEventsAsync();
    }
}
