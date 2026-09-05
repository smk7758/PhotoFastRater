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

    public ObservableCollection<EventCandidate> PreviewCandidates { get; } = [];

    [ObservableProperty]
    private string _previewStatus = "自動候補はまだ作成されていません。";

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
        var photos = await _photoRepository.GetAllAsync();
        var candidates = _eventService.PreviewAutoGroups(photos, TimeSpan.FromHours(2));
        PreviewCandidates.Clear();
        foreach (var candidate in candidates)
            PreviewCandidates.Add(candidate);
        PreviewStatus = $"{candidates.Count:N0}件の候補を確認してください。まだDBは変更していません。";
    }

    [RelayCommand]
    private async Task ConfirmAutoGroupsAsync()
    {
        var created = await _eventService.ConfirmAutoGroupsAsync(PreviewCandidates);
        PreviewStatus = $"{created:N0}件を作成しました。既存候補は重複登録していません。";
        PreviewCandidates.Clear();
        await LoadEventsAsync();
    }
}
