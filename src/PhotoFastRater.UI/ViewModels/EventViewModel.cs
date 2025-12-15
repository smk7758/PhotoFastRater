using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using PhotoFastRater.Core.Database.Repositories;
using PhotoFastRater.Core.Models;
using PhotoFastRater.Core.Services;

namespace PhotoFastRater.UI.ViewModels;

public partial class EventViewModel : ViewModelBase
{
    private readonly EventRepository _eventRepository;
    private readonly EventManagementService _eventService;
    private readonly IServiceProvider _serviceProvider;

    [ObservableProperty]
    private ObservableCollection<Event> _events = new();

    [ObservableProperty]
    private Event? _selectedEvent;

    [ObservableProperty]
    private string _newEventName = string.Empty;

    public EventViewModel(EventRepository eventRepository, EventManagementService eventService, IServiceProvider serviceProvider)
    {
        _eventRepository = eventRepository;
        _eventService = eventService;
        _serviceProvider = serviceProvider;
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
    private async Task CreateEventAsync(List<int> photoIds)
    {
        if (string.IsNullOrWhiteSpace(NewEventName))
            return;

        await _eventService.CreateEventAsync(NewEventName, photoIds);
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
        var photoRepo = _serviceProvider.GetRequiredService<PhotoRepository>();
        var photos = await photoRepo.GetAllAsync();
        await _eventService.AutoGroupByProximityAsync(photos, TimeSpan.FromHours(2));
        await LoadEventsAsync();
    }
}
