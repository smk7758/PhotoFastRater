using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PhotoFastRater.Core.Abstractions;
using PhotoFastRater.Core.Models;
using PhotoFastRater.Infrastructure.Database.Repositories;

namespace PhotoFastRater.UI.ViewModels;

/// <summary>Builds bounded tag and collection UI state from normalized catalog entities.</summary>
public partial class LibraryOrganizationViewModel(
    LibraryOrganizationRepository repository,
    IUserInteractionService interaction) : ViewModelBase
{
    public ObservableCollection<PhotoTag> Tags { get; } = [];
    public ObservableCollection<CollectionNodeViewModel> Collections { get; } = [];

    [ObservableProperty]
    private CollectionNodeViewModel? _selectedCollection;

    [ObservableProperty]
    private string _newCollectionName = string.Empty;

    [ObservableProperty]
    private string _statusText = "タグまたはコレクションを選択してください。";

    /// <summary>Refreshes only organization entities, not their photo relationship graphs.</summary>
    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        var tags = await repository.GetTagsAsync(cancellationToken);
        Tags.Clear();
        foreach (var tag in tags)
            Tags.Add(tag);

        var collections = await repository.GetCollectionsAsync(cancellationToken);
        BuildTree(collections);
    }

    /// <summary>Prompts for one tag and applies it idempotently to the bounded UI selection.</summary>
    public async Task AddTagAsync(IReadOnlyCollection<int> photoIds)
    {
        var name = await interaction.PromptTextAsync("タグを追加", "選択写真に付けるタグ名を入力してください。");
        if (string.IsNullOrWhiteSpace(name))
            return;
        await repository.AddTagAsync(name, photoIds);
        await LoadAsync();
        StatusText = $"{photoIds.Count:N0}枚に「{name.Trim()}」を追加しました。";
    }

    /// <summary>Adds the bounded UI selection to the selected collection.</summary>
    public async Task AddToSelectedCollectionAsync(IReadOnlyCollection<int> photoIds)
    {
        if (SelectedCollection is null)
            throw new InvalidOperationException("追加先のコレクションを選択してください。");
        await repository.AddToCollectionAsync(SelectedCollection.Id, photoIds);
        StatusText = $"{photoIds.Count:N0}枚を「{SelectedCollection.Name}」へ追加しました。";
    }

    [RelayCommand]
    private async Task CreateRootCollectionAsync() => await CreateCollectionAsync(null);

    [RelayCommand]
    private async Task CreateChildCollectionAsync()
    {
        if (SelectedCollection is null)
        {
            await interaction.NotifyAsync("コレクション", "親にするコレクションを選択してください。", UserNotificationKind.Warning);
            return;
        }
        await CreateCollectionAsync(SelectedCollection.Id);
    }

    private async Task CreateCollectionAsync(int? parentId)
    {
        if (string.IsNullOrWhiteSpace(NewCollectionName))
            return;
        try
        {
            await repository.CreateCollectionAsync(NewCollectionName, parentId);
            NewCollectionName = string.Empty;
            await LoadAsync();
            StatusText = "コレクションを作成しました。";
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or KeyNotFoundException)
        {
            await interaction.NotifyAsync("コレクション", exception.Message, UserNotificationKind.Warning);
        }
    }

    private void BuildTree(IReadOnlyList<PhotoCollection> items)
    {
        var nodes = items.ToDictionary(item => item.Id, item => new CollectionNodeViewModel(item.Id, item.Name, item.ParentId));
        foreach (var node in nodes.Values)
        {
            if (node.ParentId is int parentId && nodes.TryGetValue(parentId, out var parent))
                parent.Children.Add(node);
        }
        Collections.Clear();
        foreach (var root in nodes.Values.Where(node => node.ParentId is null).OrderBy(node => node.Name))
            Collections.Add(root);
    }
}

/// <summary>Represents one collection node without retaining mapped photos.</summary>
public sealed class CollectionNodeViewModel(int id, string name, int? parentId)
{
    public int Id { get; } = id;
    public string Name { get; } = name;
    public int? ParentId { get; } = parentId;
    public ObservableCollection<CollectionNodeViewModel> Children { get; } = [];
}
